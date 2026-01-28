#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property TrimMode=full
#:property IlcOptimizationPreference=Size

#:property InvariantGlobalization=true

#:property DebugType=none
#:property StripSymbols=true

#:package Aprillz.MewUI@0.8.5

using System.Globalization;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

// GDI is Windows-only; fall back to OpenGL on Linux.
Application.DefaultGraphicsBackend = OperatingSystem.IsWindows()
    ? GraphicsBackend.Gdi
    : GraphicsBackend.OpenGL;

Button equalsButton = null!;

var expression = new ObservableValue<string>(string.Empty);
var result = new ObservableValue<string>("0");
var error = new ObservableValue<string>(string.Empty);


UniformGrid Keypad() => new UniformGrid()
    .Rows(5)
    .Columns(4)
    .Children(
        KeyButton("C", Clear),
        KeyButton("(", () => Append("(")),
        KeyButton(")", () => Append(")")),
        KeyButton("⌫", Backspace),

        KeyButton("7", () => Append("7")),
        KeyButton("8", () => Append("8")),
        KeyButton("9", () => Append("9")),
        KeyButton("/", () => Append("/")),

        KeyButton("4", () => Append("4")),
        KeyButton("5", () => Append("5")),
        KeyButton("6", () => Append("6")),
        KeyButton("*", () => Append("*")),

        KeyButton("1", () => Append("1")),
        KeyButton("2", () => Append("2")),
        KeyButton("3", () => Append("3")),
        KeyButton("-", () => Append("-")),

        KeyButton("0", () => Append("0")),
        KeyButton(".", () => Append(".")),
        KeyButton("=", CommitEquals).Ref(out equalsButton),
        KeyButton("+", () => Append("+"))
    );

var window = new Window()
    .OnPreviewKeyDown(e =>
    {
        if (e.Key == Key.Backspace)
        {
            Backspace();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Clear();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            CommitEquals();
            e.Handled = true;
        }
    })
    .OnPreviewTextInput(e =>
    {
        if (TryAppendFromText(e.Text))
            e.Handled = true;
    })
    .Title("MewUI FBA Calculator")
    .Size(360, 520)
    .OnLoaded(() =>
    {
        var goldAccent = Color.FromRgb(214, 176, 82);
        Application.Current.Theme = Theme.Dark.WithAccent(goldAccent);
        equalsButton.Focus();
    })
    .Content(
        new DockPanel()
            .Margin(8)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Spacing(6)
                    .Children(
                        new Label()
                            .BindText(expression, s => string.IsNullOrEmpty(s) ? " " : s)
                            .TextWrapping(TextWrapping.Wrap)
                            .FontSize(16),

                        new Label()
                            .BindText(result)
                            .FontSize(28)
                            .Bold()
                            .TextAlignment(TextAlignment.Right),

                        new Label()
                            .BindText(error, s => string.IsNullOrEmpty(s) ? " " : s)
                            .TextWrapping(TextWrapping.Wrap)
                    ),

                Keypad()
            )
    );

Recompute();

Application.Run(window);


void Recompute()
{
    if (string.IsNullOrWhiteSpace(expression.Value))
    {
        result.Value = "0";
        error.Value = string.Empty;
        return;
    }

    try
    {
        var value = ExpressionEvaluator.Evaluate(expression.Value);
        result.Value = value.ToString("G15", CultureInfo.InvariantCulture);
        error.Value = string.Empty;
    }
    catch (Exception ex)
    {
        result.Value = "—";
        error.Value = ex.Message;
    }
}

void Append(string token)
{
    if (string.IsNullOrEmpty(token))
        return;

    expression.Value += token;
    Recompute();
}

void Clear()
{
    expression.Value = string.Empty;
    result.Value = "0";
    error.Value = string.Empty;
}

void Backspace()
{
    if (string.IsNullOrEmpty(expression.Value))
        return;

    expression.Value = expression.Value[..^1];
    Recompute();
}

void CommitEquals()
{
    if (!string.IsNullOrEmpty(error.Value))
        return;

    expression.Value = result.Value;
    Recompute();
}

bool TryAppendFromText(string text)
{
    if (string.IsNullOrEmpty(text))
        return false;

    bool appended = false;
    for (int i = 0; i < text.Length; i++)
    {
        char c = text[i];
        if (c is >= '0' and <= '9' or '.' or '+' or '-' or '*' or '/' or '(' or ')')
        {
            Append(c.ToString());
            appended = true;
        }
    }

    return appended;
}

Button KeyButton(string text, Action onClick)
{
    var b = new Button()
        .Content(text)
        .Margin(2)
        .FontSize(20)
        .OnClick(onClick)
        .MinWidth(56)
        .MinHeight(44);

    return b;
}

static class ExpressionEvaluator
{
    public static double Evaluate(string input)
    {
        var tokens = Tokenize(input);
        var rpn = ToRpn(tokens);
        return EvalRpn(rpn);
    }

    private enum TokenKind
    {
        Number,
        Operator,
        LParen,
        RParen,
    }

    private readonly record struct Token(TokenKind Kind, double Number, char Op)
    {
        public static Token Num(double value) => new(TokenKind.Number, value, '\0');
        public static Token Oper(char op) => new(TokenKind.Operator, 0, op);
        public static Token LParen() => new(TokenKind.LParen, 0, '\0');
        public static Token RParen() => new(TokenKind.RParen, 0, '\0');
    }

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        var number = new StringBuilder();

        bool lastWasValue = false; // number or ')'
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
                continue;

            if (char.IsDigit(c) || c == '.')
            {
                number.Append(c);
                continue;
            }

            FlushNumber();

            if (c == '(')
            {
                tokens.Add(Token.LParen());
                lastWasValue = false;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(Token.RParen());
                lastWasValue = true;
                continue;
            }

            if (IsOperator(c))
            {
                // Unary minus: treat "-x" as "0 - x".
                if (c == '-' && !lastWasValue)
                    tokens.Add(Token.Num(0));

                tokens.Add(Token.Oper(c));
                lastWasValue = false;
                continue;
            }

            throw new FormatException($"Invalid character '{c}'.");
        }

        FlushNumber();
        return tokens;

        void FlushNumber()
        {
            if (number.Length == 0)
                return;

            if (!double.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Invalid number '{number}'.");

            tokens.Add(Token.Num(value));
            number.Clear();
            lastWasValue = true;
        }
    }

    private static bool IsOperator(char c) => c is '+' or '-' or '*' or '/';

    private static int Precedence(char op) => op is '*' or '/' ? 2 : 1;

    private static List<Token> ToRpn(List<Token> tokens)
    {
        var output = new List<Token>(tokens.Count);
        var stack = new Stack<Token>();

        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case TokenKind.Number:
                    output.Add(t);
                    break;

                case TokenKind.Operator:
                    while (stack.Count > 0 && stack.Peek().Kind == TokenKind.Operator)
                    {
                        var top = stack.Peek();
                        if (Precedence(top.Op) < Precedence(t.Op))
                            break;
                        output.Add(stack.Pop());
                    }
                    stack.Push(t);
                    break;

                case TokenKind.LParen:
                    stack.Push(t);
                    break;

                case TokenKind.RParen:
                    while (stack.Count > 0 && stack.Peek().Kind != TokenKind.LParen)
                        output.Add(stack.Pop());

                    if (stack.Count == 0 || stack.Peek().Kind != TokenKind.LParen)
                        throw new FormatException("Mismatched parentheses.");

                    stack.Pop(); // '('
                    break;
            }
        }

        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (t.Kind is TokenKind.LParen or TokenKind.RParen)
                throw new FormatException("Mismatched parentheses.");
            output.Add(t);
        }

        return output;
    }

    private static double EvalRpn(List<Token> tokens)
    {
        var values = new Stack<double>();

        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Number)
            {
                values.Push(t.Number);
                continue;
            }

            if (t.Kind != TokenKind.Operator)
                throw new FormatException("Invalid expression.");

            if (values.Count < 2)
                throw new FormatException("Invalid expression.");

            var b = values.Pop();
            var a = values.Pop();
            values.Push(t.Op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b == 0 ? throw new DivideByZeroException("Division by zero.") : a / b,
                _ => throw new FormatException($"Unknown operator '{t.Op}'."),
            });
        }

        if (values.Count != 1)
            throw new FormatException("Invalid expression.");

        return values.Pop();
    }
}
