// Ported from Windows Terminal src/terminal/parser/ut_parser/StateMachineTest.cpp (MIT).

using System.Text;

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Parser;

namespace MewUI.Terminal.Test.Parser;

[TestClass]
public sealed class StateMachineTests
{
    private sealed class TestStateMachineEngine : IStateMachineEngine
    {
        public ulong CsiId;
        public List<int> CsiParams = new();
        public Func<bool>? FlushToTerminal;
        public StringBuilder PassedThrough = new();
        public StringBuilder Printed = new();
        public StringBuilder Executed = new();
        public ulong DcsId;
        public List<int> DcsParams = new();
        public StringBuilder DcsDataString = new();

        public void ResetTestState()
        {
            Printed.Clear();
            PassedThrough.Clear();
            Executed.Clear();
            CsiId = 0;
            CsiParams.Clear();
            DcsId = 0;
            DcsParams.Clear();
            DcsDataString.Clear();
        }

        public void UnknownSequence()
        {
        }

        public bool EncounteredWin32InputModeSequence() => false;

        public bool ActionExecute(char character)
        {
            Executed.Append(character);
            return true;
        }

        public bool ActionExecuteFromEscape(char character) => true;

        public bool ActionPrint(char character) => true;

        public bool ActionPrintString(ReadOnlySpan<char> text)
        {
            Printed.Append(text);
            return true;
        }

        public bool ActionPassThroughString(ReadOnlySpan<char> text)
        {
            PassedThrough.Append(text);
            return true;
        }

        public bool ActionEscDispatch(VTID id) => true;

        public bool ActionVt52EscDispatch(VTID id, VTParameters parameters) => true;

        public bool ActionOscDispatch(int parameter, ReadOnlySpan<char> text)
        {
            FlushToTerminal?.Invoke();
            return true;
        }

        public bool ActionSs3Dispatch(char character, VTParameters parameters) => true;

        public bool ActionCsiDispatch(VTID id, VTParameters parameters)
        {
            if (FlushToTerminal != null)
            {
                FlushToTerminal();
                return true;
            }

            CsiId = id;
            for (int index = 0; index < parameters.Size; index++)
            {
                CsiParams.Add(parameters.At(index).ValueOr(0));
            }

            return true;
        }

        public StringHandler? ActionDcsDispatch(VTID id, VTParameters parameters)
        {
            DcsId = id;
            for (int index = 0; index < parameters.Size; index++)
            {
                DcsParams.Add(parameters.At(index).ValueOr(0));
            }

            DcsDataString.Clear();
            return character =>
            {
                DcsDataString.Append(character);
                return true;
            };
        }
    }

    private static (TestStateMachineEngine Engine, StateMachine Machine) Create(bool withFlush)
    {
        var engine = new TestStateMachineEngine();
        var machine = new StateMachine(engine);
        if (withFlush)
        {
            engine.FlushToTerminal = machine.FlushToTerminal;
        }

        return (engine, machine);
    }

    [TestMethod]
    public void TwoStateMachinesDoNotInterfereWithEachOther()
    {
        var (firstEngine, firstMachine) = Create(false);
        var (secondEngine, secondMachine) = Create(false);

        firstMachine.ProcessString("\u001b[12");
        secondMachine.ProcessString("\u001b[3C");
        firstMachine.ProcessString(";34m");

        CollectionAssert.AreEqual(new[] { 12, 34 }, firstEngine.CsiParams);
        CollectionAssert.AreEqual(new[] { 3 }, secondEngine.CsiParams);
    }

    [TestMethod]
    public void PassThroughUnhandled()
    {
        var (engine, machine) = Create(true);

        machine.ProcessString("\u001b[?999h 12345 Hello World");

        Assert.AreEqual("\u001b[?999h", engine.PassedThrough.ToString());
        Assert.AreEqual(" 12345 Hello World", engine.Printed.ToString());
    }

    [TestMethod]
    public void RunStorageBeforeEscape()
    {
        var (engine, machine) = Create(true);

        machine.ProcessString("12345 Hello World\u001b[?999h");

        Assert.AreEqual("12345 Hello World", engine.Printed.ToString());
        Assert.AreEqual("\u001b[?999h", engine.PassedThrough.ToString());
    }

    [TestMethod]
    public void BulkTextPrint()
    {
        var (engine, machine) = Create(false);

        machine.ProcessString("12345 Hello World");

        Assert.AreEqual("12345 Hello World", engine.Printed.ToString());
    }

    [TestMethod]
    public void PassThroughUnhandledSplitAcrossWrites()
    {
        var (engine, machine) = Create(true);

        machine.ProcessString("\u001b[?12");
        Assert.AreEqual(string.Empty, engine.PassedThrough.ToString());
        Assert.AreEqual(string.Empty, engine.Printed.ToString());

        machine.ProcessString("34h");
        Assert.AreEqual("\u001b[?1234h", engine.PassedThrough.ToString());
        Assert.AreEqual(string.Empty, engine.Printed.ToString());

        engine.ResetTestState();

        machine.ProcessString("\u001b[?2");
        Assert.AreEqual(string.Empty, engine.PassedThrough.ToString());
        machine.ProcessString("34");
        Assert.AreEqual(string.Empty, engine.PassedThrough.ToString());
        machine.ProcessString("5h");
        Assert.AreEqual("\u001b[?2345h", engine.PassedThrough.ToString());
        Assert.AreEqual(string.Empty, engine.Printed.ToString());

        engine.ResetTestState();

        // Split inside the OSC terminator.
        machine.ProcessString("\u001b]99;foo\u001b");
        Assert.AreEqual(string.Empty, engine.PassedThrough.ToString());
        machine.ProcessString("\\");
        Assert.AreEqual("\u001b]99;foo\u001b\\", engine.PassedThrough.ToString());
        Assert.AreEqual(string.Empty, engine.Printed.ToString());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void DcsDataStringsReceivedByHandler(int terminatorType)
    {
        var (engine, machine) = Create(false);

        ulong expectedCsiId = 0;
        string expectedExecuted = string.Empty;
        string terminator;
        if (terminatorType == 0)
        {
            terminator = "\u001b\\";
        }
        else if (terminatorType == 1)
        {
            terminator = "\u001b[m";
            expectedCsiId = 'm';
        }
        else if (terminatorType == 2)
        {
            terminator = "\u0018";
            expectedExecuted = "\u0018";
        }
        else
        {
            terminator = "\u001a";
            expectedExecuted = "\u001a";
        }

        machine.ProcessString("\u001bP1;2;3|data string");
        machine.ProcessString(terminator);
        machine.ProcessString("printed text");

        Assert.AreEqual(VTID.FromString("|").Value, engine.DcsId);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, engine.DcsParams);
        Assert.AreEqual("data string\u001b", engine.DcsDataString.ToString());
        Assert.AreEqual("printed text", engine.Printed.ToString());
        Assert.AreEqual(expectedCsiId, engine.CsiId);
        Assert.AreEqual(expectedExecuted, engine.Executed.ToString());
    }

    [TestMethod]
    public void VtParameterSubspanTest()
    {
        VTParameter[] parameterList = [12, 34, 56, 78];
        var parameterSpan = new VTParameters(parameterList);

        var subspan = parameterSpan.Subspan(0);
        Assert.AreEqual(4, subspan.Size);
        Assert.AreEqual(12, (int)subspan.At(0));
        Assert.AreEqual(34, (int)subspan.At(1));
        Assert.AreEqual(56, (int)subspan.At(2));
        Assert.AreEqual(78, (int)subspan.At(3));

        subspan = parameterSpan.Subspan(2);
        Assert.AreEqual(2, subspan.Size);
        Assert.AreEqual(56, (int)subspan.At(0));
        Assert.AreEqual(78, (int)subspan.At(1));

        subspan = parameterSpan.Subspan(4);
        Assert.AreEqual(1, subspan.Size);
        Assert.IsFalse(subspan.At(0).HasValue);

        subspan = parameterSpan.Subspan(6);
        Assert.AreEqual(1, subspan.Size);
        Assert.IsFalse(subspan.At(0).HasValue);
    }
}
