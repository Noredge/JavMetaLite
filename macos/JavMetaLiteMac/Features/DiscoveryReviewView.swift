import SwiftUI
import AppKit
#if SWIFT_PACKAGE
import WorkerKit
import NativeBridgeKit
#endif

struct DiscoveryReviewView: View {
    @EnvironmentObject var store: WorkspaceStore
    @ViewState private var showingDetails = false

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(tr("discoveryTitle")).font(.title2.bold()).accessibilityAddTraits(.isHeader)
            Text(tr("discoveryExplanation")).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if let review = store.discoveryReview {
                HStack {
                    Button(tr("discoverySelectAll")) { store.selectAllDiscovered() }
                    Button(tr("discoveryClearSelection")) { store.deselectAllDiscovered() }
                    Button(tr("discoverySelectRecognized")) { store.selectRecognizedDiscovered() }.disabled(review.recognizedIDs.isEmpty)
                    Spacer()
                }.disabled(store.working || store.statusUnconfirmed || review.submitted)
                Text(String(format: tr("discoverySelectedCount"), review.selectedIDs.count, review.availableIDs.count)).font(.callout.monospacedDigit())
                    .accessibilityIdentifier("discoverySelectionCount")
                if review.discovery.items.isEmpty {
                    ContentUnavailableView(tr("discoveryEmpty"), systemImage: "doc.text.magnifyingglass")
                } else {
                    List(review.discovery.items) { item in
                        discoveryRow(item, review: review)
                    }.listStyle(.bordered).accessibilityLabel(tr("discoveryTitle"))
                }
                if !store.discoveryDetails.isEmpty || !store.discoveryErrorDetails.isEmpty {
                    DisclosureGroup(tr("discoveryWarnings"), isExpanded: $showingDetails) {
                        NativeDiagnosticTextView(text: [store.discoveryDetails, store.discoveryErrorDetails].filter { !$0.isEmpty }.joined(separator: "\n\n"), accessibilityLabel: tr("technicalDetails"))
                            .frame(height: 100)
                        HStack {
                            Button(tr("copyDetails")) {
                                let details = [store.discoveryDetails, store.discoveryErrorDetails].filter { !$0.isEmpty }.joined(separator: "\n\n")
                                if !DiagnosticClipboard.copy(details) { NSSound.beep() }
                            }
                            Spacer()
                        }
                    }
                }
                if let error = store.discoveryErrorMessage {
                    Label(error, systemImage: "exclamationmark.triangle").foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                }
                if store.statusUnconfirmed {
                    Text(tr("statusUnconfirmed")).foregroundStyle(.orange)
                } else if review.submitted && !store.working {
                    Text(tr("discoveryExpired")).font(.callout).foregroundStyle(.secondary)
                }
                HStack {
                    if store.working {
                        ProgressView().controlSize(.small)
                        Text(tr(store.phase == "discardingdiscovery" ? "discoveryDiscarding" : "discoveryImporting")).font(.callout)
                        Spacer()
                        if store.operationID != nil { Button(tr("cancelOperation")) { store.cancel() }.keyboardShortcut(.cancelAction) }
                    } else if store.statusUnconfirmed {
                        Spacer()
                        Button(tr("checkStatus")) { store.checkStatus() }
                    } else {
                        Button(tr("cancel"), role: .cancel) { store.discardDiscovery() }.keyboardShortcut(.cancelAction)
                        Spacer()
                        if review.submitted {
                            Button(tr("discoveryRescan")) { store.rescanDiscovery() }.keyboardShortcut(.defaultAction)
                        } else {
                            Button(String(format: tr("discoveryAddSelected"), review.selectedIDs.count)) { store.confirmDiscovery() }
                                .keyboardShortcut(.defaultAction).disabled(!review.canConfirm)
                                .accessibilityIdentifier("confirmDiscovery")
                        }
                    }
                }
            }
        }
        .padding(22).frame(width: 790, height: 520)
        .interactiveDismissDisabled()
    }

    private func discoveryRow(_ item: DiscoveredMovie, review: InputDiscoveryReview) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Toggle(isOn: Binding(get: { review.selectedIDs.contains(item.itemID) }, set: { store.selectDiscovered(item.itemID, selected: $0) })) { EmptyView() }
                .toggleStyle(.checkbox).labelsHidden()
                .accessibilityLabel(tr("discoveryInclude") + ": " + URL(fileURLWithPath: item.primaryPath).lastPathComponent)
                .accessibilityIdentifier("discoveryItem-" + item.itemID)
                .disabled(!item.canImport || store.working || store.statusUnconfirmed || review.submitted)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline) {
                    Text(URL(fileURLWithPath: item.primaryPath).lastPathComponent).font(.headline)
                    Spacer(minLength: 8)
                    if item.videoPaths.count > 1 { Text(String(format: tr("discoveryParts"), item.videoPaths.count)).font(.caption).foregroundStyle(.secondary) }
                }
                Text(item.movieID.isEmpty ? tr(item.canImport ? "discoveryUnknownID" : "unknownID") : item.movieID).font(.callout)
                Text(item.primaryPath).font(.caption).foregroundStyle(.secondary).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                if item.videoPaths.count > 1 {
                    DisclosureGroup(tr("discoveryVideoFiles")) {
                        ForEach(item.videoPaths, id: \.self) { Text($0).font(.caption).textSelection(.enabled).fixedSize(horizontal: false, vertical: true) }
                    }
                }
                if !item.canImport { Text(unavailableLabel(item)).font(.caption).foregroundStyle(.secondary) }
            }
        }.padding(.vertical, 5)
    }

    private func unavailableLabel(_ item: DiscoveredMovie) -> String {
        if item.alreadyImported || item.unavailableReason == "ALREADY_IMPORTED" { return tr("discoveryAlreadyImported") }
        let key = "discoveryUnavailableReason." + item.unavailableReason
        return tr(key) == key ? tr("discoveryUnavailable") : tr(key)
    }
}
