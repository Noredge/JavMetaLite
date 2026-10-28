import SwiftUI
import AppKit
#if SWIFT_PACKAGE
import WorkerKit
import NativeBridgeKit
#endif

@main struct JavMetaLiteApp: App {
    @StateObject private var store = WorkspaceStore()
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    var body: some Scene {
        Window("JavMetaLite", id: "workspace") {
            WorkspaceView().environmentObject(store)
                .frame(minWidth: 920, minHeight: 620)
                .background(WindowBridge(store: store))
                .task { delegate.store = store; await store.start() }
        }
        .defaultSize(width: 1280, height: 820)
        .windowToolbarStyle(.unified)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(replacing: .newItem) {
                Button(tr("addMovies")) { store.chooseFiles() }.keyboardShortcut("o").disabled(!store.canImportFiles)
            }
            CommandGroup(replacing: .saveItem) {
                Button(String(format: tr("saveActionCount"), store.actionTargetCount)) { store.saveActionTargets() }.keyboardShortcut("s").disabled(!store.canPreviewActionTargets)
            }
            CommandMenu(tr("metadata")) {
                Button(String(format: tr("queryAction.checked"), store.actionTargetCount)) { store.searchActionTargets() }.keyboardShortcut("r").disabled(!store.canSearchActionTargets)
                Button(tr("retrySources")) { store.search(retry: true) }.disabled(!store.canRetrySources)
                Button(tr("manualBrowser")) { store.openBrowser() }.keyboardShortcut("b", modifiers: [.command, .option]).disabled(!store.canOpenBrowser)
                Divider()
                Button(tr("cancelOperation")) { store.cancel() }.keyboardShortcut(".").disabled(store.operationID == nil)
                Button(tr("findQueue")) { NotificationCenter.default.post(name: .focusQueueSearch, object: nil) }.keyboardShortcut("f")
            }
            CommandGroup(after: .newItem) {
                Button(tr("clearQueueShort")) { store.requestClearQueue() }
                    .keyboardShortcut(.delete, modifiers: [.command, .option]).disabled(!store.canClearQueue)
            }
            CommandGroup(after: .sidebar) {
                Button(tr("toggleQueue")) { store.queueVisible.toggle() }.keyboardShortcut("l", modifiers: [.command, .option])
                Button(tr("inspector")) { store.inspectorVisible.toggle() }.keyboardShortcut("i", modifiers: [.command, .option])
            }
        }
        Settings { SettingsView().environmentObject(store).frame(width: 490, height: 530) }
    }
}

extension Notification.Name { static let focusQueueSearch = Notification.Name("JavMetaLite.focusQueueSearch") }

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
    weak var store: WorkspaceStore?
    private var awaitingExit = false
    func applicationDidFinishLaunching(_ notification: Notification) {
        if let url = Bundle.main.url(forResource: "JavMetaLite", withExtension: "icns"), let icon = NSImage(contentsOf: url) {
            NSApp.applicationIconImage = icon
        }
    }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard let store else { return .terminateNow }
        if awaitingExit { return .terminateLater }
        if store.hasPendingArtworkSelections {
            Task { await store.waitForArtworkSelections(); sender.terminate(nil) }
            return .terminateCancel
        }
        guard store.commitEditing() else { return .terminateCancel }
        if store.engineProcessExited {
            return store.confirmEndExitedEngineSession() ? .terminateNow : .terminateCancel
        }
        if store.working || store.statusUnconfirmed {
            let alert = NSAlert(); alert.messageText = tr(store.statusUnconfirmed ? "statusUnconfirmed" : "operationInProgress"); alert.informativeText = tr(store.statusUnconfirmed ? "unconfirmedStopMessage" : "safeStopMessage")
            alert.addButton(withTitle: tr("keepOpen")); alert.addButton(withTitle: tr(store.statusUnconfirmed ? "checkStatus" : "cancelOperation"))
            alert.configureSafeCancelDefault()
            if alert.runSafeModal() == .alertSecondButtonReturn { if store.statusUnconfirmed { store.checkStatus() } else { store.cancel() } }
            return .terminateCancel
        }
        if store.unsavedCount > 0 {
            let alert = NSAlert(); alert.messageText = tr("unsavedTitle")
            alert.informativeText = String(format: tr("unsavedMessage"), store.unsavedCount)
            alert.addButton(withTitle: tr("keepEditing")); alert.addButton(withTitle: tr("discardQuit")); alert.addButton(withTitle: tr("reviewUnsaved"))
            alert.configureSafeCancelDefault()
            switch alert.runSafeModal() {
            case .alertSecondButtonReturn: break
            case .alertThirdButtonReturn:
                if let id = store.jobs.first(where: { $0.hasUnsavedChanges || !(store.drafts[$0.jobID] ?? [:]).isEmpty })?.jobID { store.selectNavigation([id]) }
                NSApp.windows.first(where: { $0.title == "JavMetaLite" })?.makeKeyAndOrderFront(nil)
                return .terminateCancel
            default: return .terminateCancel
            }
        }
        awaitingExit = true
        Task { await store.worker.shutdown(); NSApp.reply(toApplicationShouldTerminate: true) }
        return .terminateLater
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { sender.windows.first(where: { $0.title == "JavMetaLite" })?.makeKeyAndOrderFront(nil) }
        return true
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

struct WindowBridge: NSViewRepresentable {
    let store: WorkspaceStore
    func makeCoordinator() -> Coordinator { Coordinator(store: store) }
    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async {
            guard let window = view.window else { return }
            store.workspaceWindow = window
            store.setWorkspaceSheetPresented(window.attachedSheet != nil)
            // The unified toolbar and shared action/status bars align across every pane.
            window.titlebarSeparatorStyle = .none
            window.titleVisibility = .hidden
            context.coordinator.previousDelegate = window.delegate
            window.delegate = context.coordinator
            window.setFrameAutosaveName("JavMetaLiteWorkspace")
            window.setFrameUsingName("JavMetaLiteWorkspace")
            WorkspaceWindowPolicy.constrainRestoredFrame(window)
        }
        return view
    }
    func updateNSView(_ nsView: NSView, context: Context) {}
    @MainActor final class Coordinator: NSObject, NSWindowDelegate {
        let store: WorkspaceStore
        weak var previousDelegate: NSWindowDelegate?
        init(store: WorkspaceStore) { self.store = store }
        override func responds(to selector: Selector!) -> Bool { super.responds(to: selector) || previousDelegate?.responds(to: selector) == true }
        override func forwardingTarget(for selector: Selector!) -> Any? {
            if previousDelegate?.responds(to: selector) == true { return previousDelegate }
            return super.forwardingTarget(for: selector)
        }
        func windowWillBeginSheet(_ notification: Notification) {
            store.setWorkspaceSheetPresented(true)
            previousDelegate?.windowWillBeginSheet?(notification)
        }
        func windowDidEndSheet(_ notification: Notification) {
            // Forwarding may immediately present another sheet; its begin event
            // must remain authoritative instead of being cleared by this end.
            store.setWorkspaceSheetPresented(false)
            previousDelegate?.windowDidEndSheet?(notification)
        }
        func windowShouldClose(_ sender: NSWindow) -> Bool {
            if store.hasPendingArtworkSelections {
                Task { await store.waitForArtworkSelections(); sender.performClose(nil) }
                return false
            }
            guard store.commitEditing() else { return false }
            if store.engineProcessExited { return store.confirmEndExitedEngineSession(closingWindow: true) }
            if store.working || store.statusUnconfirmed {
                let alert = NSAlert(); alert.messageText = tr(store.statusUnconfirmed ? "statusUnconfirmed" : "operationInProgress"); alert.informativeText = tr(store.statusUnconfirmed ? "unconfirmedStopMessage" : "safeStopMessage")
                alert.addButton(withTitle: tr("keepOpen")); alert.addButton(withTitle: tr(store.statusUnconfirmed ? "checkStatus" : "cancelOperation"))
                alert.configureSafeCancelDefault()
                if alert.runSafeModal() == .alertSecondButtonReturn { if store.statusUnconfirmed { store.checkStatus() } else { store.cancel() } }
                return false
            }
            if store.unsavedCount > 0 {
                let alert = NSAlert(); alert.messageText = tr("unsavedTitle"); alert.informativeText = tr("closeWindowMessage")
                alert.addButton(withTitle: tr("keepEditing")); alert.addButton(withTitle: tr("closeWindow"))
                alert.configureSafeCancelDefault()
                return alert.runSafeModal() == .alertSecondButtonReturn
            }
            return true
        }
    }
}
