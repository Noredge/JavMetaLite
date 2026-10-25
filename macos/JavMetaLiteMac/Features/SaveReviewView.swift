import SwiftUI
#if SWIFT_PACKAGE
import WorkerKit
#endif

struct SaveReviewView: View {
    @EnvironmentObject var store: WorkspaceStore
    let preview: SavePreview
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack { Image(systemName: "checkmark.rectangle").font(.title).foregroundStyle(.tint); VStack(alignment: .leading, spacing: 4) {
                Text(tr("reviewSaveTitle")).font(.title2).fontWeight(.semibold)
                Text(preview.combinesParts ? String(format: tr("reviewCombinedPartsCount"), preview.jobIDs.count) : String(format: tr("reviewMovieCount"), preview.jobs.count)).font(.callout).foregroundStyle(.secondary)
            } }
            Text(tr("reviewSaveConsent")).font(.callout).foregroundStyle(.secondary)
            if !preview.canExecute { Label(tr("saveBlockedSummary"), systemImage: "exclamationmark.octagon.fill").font(.callout).foregroundStyle(.orange) }
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    ForEach(preview.jobs) { group in
                        VStack(alignment: .leading, spacing: 10) {
                            Text(store.jobs.first(where: { $0.jobID == group.jobID })?.metadata["id"].stringValue ?? group.jobID).font(.headline)
                            if let job = store.jobs.first(where: { $0.jobID == group.jobID }), job.artwork.contains(where: { store.artworkPaths[store.artworkKey($0, jobID: job.jobID)] == nil }) {
                                Text(tr("saveWithoutLoadedPreviews")).font(.caption).foregroundStyle(.secondary)
                            }
                            ForEach(Array(group.changes.enumerated()), id: \.offset) { _, change in
                                HStack(alignment: .top, spacing: 14) {
                                    Text(operationLabel(change)).font(.callout.weight(.semibold)).foregroundStyle(change["conflict"].boolValue ? .red : .primary).frame(width: 90, alignment: .leading)
                                    VStack(alignment: .leading, spacing: 5) {
                                        if !change["source"].stringValue.isEmpty && change["source"] != change["target"] { Text(change["source"].stringValue).foregroundStyle(.secondary).textSelection(.enabled) }
                                        if !change["source"].stringValue.isEmpty && change["source"] != change["target"] {
                                            Label(change["target"].stringValue, systemImage: "arrow.down.right").textSelection(.enabled)
                                        } else { Text(change["target"].stringValue).textSelection(.enabled) }
                                        Text(changeReason(change)).font(.caption).foregroundStyle(.secondary)
                                        if !change["reason"].stringValue.isEmpty { DisclosureGroup(tr("technicalDetails")) { Text(change["reason"].stringValue).font(.caption).textSelection(.enabled) }.font(.caption).foregroundStyle(.secondary) }
                                    }.font(.callout).frame(maxWidth: .infinity, alignment: .leading)
                                }.padding(.vertical, 4)
                                Divider()
                            }
                            ForEach(displayWarnings(group.warnings), id: \.self) { Label($0, systemImage: "exclamationmark.triangle").font(.callout).foregroundStyle(.orange) }
                            ForEach(group.blockingConflicts, id: \.self) { conflictRow($0) }
                        }
                    }
                    ForEach(displayWarnings(preview.warnings, excluding: Set(preview.jobs.flatMap { $0.warnings }.map(warningLabel))), id: \.self) { Label($0, systemImage: "exclamationmark.triangle").font(.callout).foregroundStyle(.orange) }
                    ForEach(preview.blockingConflicts, id: \.self) { conflictRow($0) }
                    if store.previewStale { Label(tr("previewStale"), systemImage: "exclamationmark.triangle").foregroundStyle(.orange) }
                }
            }
            HStack {
                Text(tr(preview.containsVideoTransfer ? "verifiedCopyNote" : "videoUnchanged")).font(.caption).foregroundStyle(.secondary).frame(maxWidth: 390, alignment: .leading)
                Spacer()
                Button(tr("cancel")) { store.preview = nil }.keyboardShortcut(.cancelAction)
                Button(tr("confirmSave")) { store.save() }.buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction)
                    .disabled(!preview.canExecute || store.previewStale || store.working)
            }
        }.padding(24).frame(width: 780, height: 560)
    }
    private func conflictRow(_ message: String) -> some View {
        HStack(alignment: .top) {
            Image(systemName: "xmark.octagon.fill").foregroundStyle(.red)
            Text(SaveReviewText.conflict(message)).foregroundStyle(.primary).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
        }.font(.callout).padding(10).background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 6))
    }
    func changeReason(_ change: JSONValue) -> String {
        if change["operation"].stringValue == "KeepFile" { return tr(isPreservedMove(change) ? "preservedMoveReason" : "keepFileReason") }
        if change["operation"].stringValue == "CopyAndVerifyVideo" { return tr("verifiedCopyNote") }
        if change["operation"].stringValue == "RemoveFile" { return tr("removePlannedFileReason") }
        let ext = URL(fileURLWithPath: change["target"].stringValue).pathExtension.lowercased()
        if ext == "nfo" { return tr("nfoChangeReason") }
        if ["jpg", "jpeg", "png", "webp"].contains(ext) { return tr("artworkChangeReason") }
        return change["reason"].stringValue
    }
    private func isPreservedMove(_ change: JSONValue) -> Bool {
        change["operation"].stringValue == "KeepFile" && !change["source"].stringValue.isEmpty && change["source"] != change["target"]
    }
    func operationLabel(_ change: JSONValue) -> String {
        if isPreservedMove(change) { return tr("operation.preservedMove") }
        let operation = change["operation"].stringValue
        let key = "operation." + operation.lowercased()
        let translated = tr(key)
        return translated == key ? operation : translated
    }
    private func warningLabel(_ warning: String) -> String {
        switch warning {
        case "Videos will be copied with full SHA-256 verification, committed, then originals retired. Real device-loss scenarios require separate validation.":
            return tr("verifiedCopyNote")
        case "NFO output is disabled; edited metadata will not be written.":
            return tr("warning.nfoDisabled")
        case "Unique secondary sidecars remain at their original paths; only the first movie's reviewed metadata is used.":
            return tr("warning.partsSidecarsPreserved")
        default: return warning
        }
    }
    private func displayWarnings(_ warnings: [String], excluding: Set<String> = []) -> [String] {
        var seen = excluding
        return warnings.map(warningLabel).filter { seen.insert($0).inserted }
    }
}

enum SaveReviewText {
    static func conflict(_ message: String, localize: (String) -> String = tr) -> String {
        let prefix = "Cross-job input/output conflict: "
        if message.hasPrefix(prefix) {
            // Only translate the known explanation; preserve the actual path,
            // including percent signs, whitespace and Unicode, byte for byte.
            return String(format: localize("conflict.crossJob"), String(message.dropFirst(prefix.count)))
        }
        if message == "Existing sidecar replacement requires explicit confirmation." {
            return localize("conflict.sidecarConsent")
        }
        return message
    }
}

struct SettingsView: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        Form {
            Section(tr("appearanceAndLanguage")) {
                Picker(tr("appearance"), selection: Binding(get: { store.appearance }, set: store.changeAppearance)) {
                    Text(tr("followSystem")).tag("system"); Text(tr("lightAppearance")).tag("light"); Text(tr("darkAppearance")).tag("dark")
                }
                Picker(tr("interfaceLanguage"), selection: Binding(get: { store.language }, set: store.changeLanguage)) {
                    ForEach(AppLanguage.allCases) { Text($0.nativeName).tag($0.rawValue) }
                }
                Text(tr("languageNote")).font(.caption).foregroundStyle(.secondary)
            }
            Section(tr("saveDefaults")) {
                Toggle(tr("includeIDTitle"), isOn: $store.includeIDInTitle)
                Toggle(tr("skipSavePreview"), isOn: $store.skipSavePreview).help(tr("skipSavePreviewHelp"))
                Text(tr(store.skipSavePreview ? "saveSafeguards" : "previewEnabledNote")).font(.caption).foregroundStyle(.secondary)
            }
            Section(tr("privacy")) {
                Text(tr("privacyMessage")).font(.callout)
                Text(tr(store.supportsArtwork ? "fullOutputNote" : "nfoOnlyNote")).font(.caption).foregroundStyle(.secondary)
                Button(tr("openLogs")) { store.openLogs() }.disabled(store.logRoot == nil)
                if let dataRoot = store.dataRoot { Text(dataRoot).font(.caption2).foregroundStyle(.secondary).textSelection(.enabled) }
            }
            Section {
                Text(tr("preferencesSavedAutomatically")).font(.caption).foregroundStyle(.secondary)
            }
        }.formStyle(.grouped).padding(8)
    }
}
