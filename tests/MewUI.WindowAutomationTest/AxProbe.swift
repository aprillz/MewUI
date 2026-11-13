// Out-of-process accessibility client for AxProviderTests. It reads and operates an application's accessibility
// tree the way VoiceOver does and prints one line per element: Role|Description|Identifier|Actions|Enabled.
import ApplicationServices
import Foundation

let arguments = CommandLine.arguments
guard arguments.count >= 3, let pid = pid_t(arguments[2]) else {
    FileHandle.standardError.write("usage: AxProbe <command> <pid> [target] [text]\n".data(using: .utf8)!)
    exit(2)
}

let command = arguments[1]
let target = arguments.count > 3 ? arguments[3] : ""
let text = arguments.count > 4 ? arguments[4] : ""
let application = AXUIElementCreateApplication(pid)
var focusEvents = 0

func attribute(_ element: AXUIElement, _ name: String) -> AnyObject? {
    var value: AnyObject?
    return AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success ? value : nil
}

func string(_ element: AXUIElement, _ name: String) -> String {
    if let value = attribute(element, name) {
        if let text = value as? String { return text }
        if let number = value as? NSNumber { return number.stringValue }
    }
    return ""
}

func children(_ element: AXUIElement) -> [AXUIElement] {
    return (attribute(element, kAXChildrenAttribute) as? [AXUIElement]) ?? []
}

func actions(_ element: AXUIElement) -> String {
    var names: CFArray?
    guard AXUIElementCopyActionNames(element, &names) == .success, let list = names as? [String] else { return "" }
    return list.sorted().joined(separator: ",")
}

func describe(_ element: AXUIElement) -> String {
    let enabled = (attribute(element, kAXEnabledAttribute) as? Bool) ?? true
    return "\(string(element, kAXRoleAttribute))|\(string(element, kAXDescriptionAttribute))|\(string(element, kAXIdentifierAttribute))|\(actions(element))|\(enabled)"
}

func dump(_ element: AXUIElement, _ depth: Int) {
    print(String(repeating: "  ", count: depth) + describe(element))
    if depth < 12 {
        for child in children(element) { dump(child, depth + 1) }
    }
}

func windows() -> [AXUIElement] {
    return (attribute(application, kAXWindowsAttribute) as? [AXUIElement]) ?? []
}

func find(_ key: String, in element: AXUIElement, depth: Int = 0) -> AXUIElement? {
    let matches = key.hasPrefix("#")
        ? string(element, kAXIdentifierAttribute) == String(key.dropFirst())
        : string(element, kAXDescriptionAttribute) == key
    if matches { return element }
    if depth > 12 { return nil }
    for child in children(element) {
        if let found = find(key, in: child, depth: depth + 1) { return found }
    }
    return nil
}

func require(_ key: String) -> AXUIElement {
    for window in windows() {
        if let found = find(key, in: window) { return found }
    }
    FileHandle.standardError.write("no element '\(key)'\n".data(using: .utf8)!)
    exit(1)
}

func check(_ error: AXError, _ what: String) {
    if error != .success {
        FileHandle.standardError.write("\(what) failed: \(error.rawValue)\n".data(using: .utf8)!)
        exit(1)
    }
}

switch command {
case "tree":
    for window in windows() where string(window, kAXTitleAttribute) == target || target.isEmpty {
        dump(window, 0)
    }
case "windows":
    for window in windows() {
        print("window \(string(window, kAXTitleAttribute))")
        dump(window, 1)
    }
case "press":
    check(AXUIElementPerformAction(require(target), kAXPressAction as CFString), "press")
    print("ok")
case "increment":
    check(AXUIElementPerformAction(require(target), kAXIncrementAction as CFString), "increment")
    print(string(require(target), kAXValueAttribute))
case "setvalue":
    check(AXUIElementSetAttributeValue(require(target), kAXValueAttribute as CFString, text as CFString), "set value")
    print("ok")
case "value":
    print(string(require(target), kAXValueAttribute))
case "select":
    check(AXUIElementSetAttributeValue(require(target), kAXSelectedAttribute as CFString, kCFBooleanTrue), "select")
    print("ok")
case "textinfo":
    let element = require(target)
    print(string(element, kAXSelectedTextAttribute))
    print(string(element, kAXNumberOfCharactersAttribute))
    var range = CFRange(location: 6, length: 5)
    let rangeValue = AXValueCreate(.cfRange, &range)!
    var word: AnyObject?
    check(AXUIElementCopyParameterizedAttributeValue(element, kAXStringForRangeParameterizedAttribute as CFString, rangeValue, &word), "string for range")
    print((word as? String) ?? "")
case "atcenter":
    let element = require(target)
    var position = CGPoint.zero
    var size = CGSize.zero
    if let value = attribute(element, kAXPositionAttribute) { AXValueGetValue(value as! AXValue, .cgPoint, &position) }
    if let value = attribute(element, kAXSizeAttribute) { AXValueGetValue(value as! AXValue, .cgSize, &size) }
    var hit: AXUIElement?
    check(AXUIElementCopyElementAtPosition(application, Float(position.x + size.width / 2), Float(position.y + size.height / 2), &hit), "hit test")
    print(describe(hit!))
case "focus":
    let count = Int(target) ?? 1
    var observer: AXObserver?
    check(AXObserverCreate(pid, { _, element, _, _ in
        print("focus " + describe(element))
        fflush(stdout)
        focusEvents += 1
    }, &observer), "observer")
    check(AXObserverAddNotification(observer!, application, kAXFocusedUIElementChangedNotification as CFString, nil), "subscribe")
    CFRunLoopAddSource(CFRunLoopGetCurrent(), AXObserverGetRunLoopSource(observer!), .defaultMode)
    print("ready")
    fflush(stdout)
    let deadline = Date().addingTimeInterval(15)
    while Date() < deadline && focusEvents < count {
        CFRunLoopRunInMode(.defaultMode, 0.2, true)
    }
default:
    FileHandle.standardError.write("unknown command '\(command)'\n".data(using: .utf8)!)
    exit(2)
}
