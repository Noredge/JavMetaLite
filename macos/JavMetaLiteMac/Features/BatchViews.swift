import SwiftUI
#if SWIFT_PACKAGE
import WorkerKit
#endif

struct BatchSummaryView: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline) {
                Text(tr("batchMode")).font(.system(size: 28, weight: .semibold, design: .serif))
                Text(String(format: tr("checkedMovieCount"), store.checkedJobIDs.count)).foregroundStyle(.secondary)
                Spacer()
                Button(tr(store.allMoviesChecked ? "uncheckAllMovies" : "checkAllMovies")) { store.toggleAllChecks() }
                    .help(tr("checkAllMoviesHelp"))
                Menu {
                    Button(tr("selectRecognizedQueue")) { store.selectRecognized() }
                    Button(tr("selectPendingQueue")) { store.selectCategory(.pending) }
                    Button(tr("selectIssuesQueue")) { store.selectCategory(.issues) }
                    Button(tr("clearChecks")) { store.setAllChecks(false) }
                    Divider()
                    Button(tr("confirmParts")) { store.reviewParts() }.disabled(!store.canReviewParts)
                } label: { Image(systemName: "ellipsis.circle") }
                    .fixedSize().accessibilityLabel(tr("queueActions"))
            }
            HStack {
                QuerySourcePicker().fixedSize().disabled(store.working || store.statusUnconfirmed)
                if store.sourceMode == "custom" {
                    Button(tr("configureSources")) { store.showingSourceProfile = true }.disabled(store.working || store.statusUnconfirmed)
                }
            }
            if store.sourceMode != "libredmm" { Text(tr("r18BatchSlowNote")).font(.caption).foregroundStyle(.secondary) }
            Divider()
            List(store.visibleJobs) { job in
                HStack {
                    Toggle(isOn: Binding(get: { store.checkedJobIDs.contains(job.jobID) },
                        set: { store.setChecked(job.jobID, checked: $0) })) {
                        QueueRow(job: job)
                    }.toggleStyle(.checkbox).accessibilityLabel(tr("includeInBatch") + " · " + store.value("id", job: job))
                    Spacer()
                    Button { store.selectNavigation([job.jobID]) } label: { Image(systemName: "arrow.up.right") }
                        .buttonStyle(.borderless).accessibilityLabel(tr("viewCurrentMovie")).help(tr("viewCurrentMovie"))
                }
            }.listStyle(.plain).scrollContentBackground(.hidden)
            .onKeyPress(keys: ["a", "A"], phases: .down) { press in
                guard press.modifiers.contains(.command) else { return .ignored }
                store.setAllChecks(!press.modifiers.contains(.shift)); return .handled
            }
        }.disabled(store.working || store.statusUnconfirmed)
    }
}

struct BatchResultView: View {
    @EnvironmentObject var store: WorkspaceStore
    @Environment(\.dismiss) var dismiss
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Label(tr("operationResults"), systemImage: "list.bullet.clipboard").font(.title2.weight(.semibold))
            Text(tr("batchIndependentCommits")).font(.callout).foregroundStyle(.secondary)
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    ForEach(Array(store.operationResults.enumerated()), id: \.offset) { _, result in
                        VStack(alignment: .leading, spacing: 6) {
                            HStack {
                                Text(store.jobLabel(result["jobId"].stringValue)).font(.headline)
                                Spacer()
                                Text(tr("result." + result["status"].stringValue.lowercased())).font(.callout)
                            }
                            ForEach(result["outputs"].arrayValue.map(\.stringValue), id: \.self) { path in
                                Button { store.reveal(path) } label: { Label(path, systemImage: "doc").font(.caption).multilineTextAlignment(.leading) }.buttonStyle(.link)
                            }
                            if !result["error"]["message"].stringValue.isEmpty { Text(result["error"]["message"].stringValue).font(.caption).foregroundStyle(.orange).textSelection(.enabled) }
                        }
                        Divider()
                    }
                    ForEach(store.operationWarnings, id: \.self) { warning in Label(warning, systemImage: "exclamationmark.triangle").font(.callout).foregroundStyle(.orange).textSelection(.enabled) }
                    ForEach(Array(store.operationCleanupIssues.enumerated()), id: \.offset) { _, issue in
                        VStack(alignment: .leading, spacing: 6) {
                            Label(tr("outcome.cleanupRequired"), systemImage: "exclamationmark.triangle").font(.callout)
                            Text(issue["path"].stringValue).font(.caption).foregroundStyle(.primary).textSelection(.enabled)
                            Text(issue["message"].stringValue).font(.caption).textSelection(.enabled)
                        }
                    }
                }
            }
            HStack { Spacer(); Button(tr("close")) { dismiss() }.keyboardShortcut(.cancelAction) }
        }.padding(24).frame(width: 740, height: 500)
    }
}

struct MultipartReviewView: View {
    @EnvironmentObject var store: WorkspaceStore
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Label(tr("confirmParts"), systemImage: "film.stack").font(.title2.weight(.semibold))
            Text(tr("partsConfirmExplanation")).font(.callout)
            if let firstID = store.partsReviewIDs.first, let first = store.jobs.first(where: { $0.jobID == firstID }) {
                Text(String(format: tr("partsMetadataSource"), store.value("id", job: first) + " · " + store.value("title", job: first))).font(.callout).foregroundStyle(.secondary)
            }
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    ForEach(Array(store.partsReviewIDs.enumerated()), id: \.element) { index, jobID in
                        if let job = store.jobs.first(where: { $0.jobID == jobID }) {
                            HStack(alignment: .top, spacing: 12) {
                                Text("CD\(index + 1)").font(.headline).frame(width: 48, alignment: .leading)
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(job.metadata["id"].stringValue).font(.headline)
                                    ForEach(job.videoPaths, id: \.self) { Text($0).font(.caption).textSelection(.enabled) }
                                }
                                Spacer()
                                Button { store.movePart(jobID, by: -1) } label: { Image(systemName: "arrow.up") }.disabled(index == 0).accessibilityLabel(tr("movePartUp"))
                                Button { store.movePart(jobID, by: 1) } label: { Image(systemName: "arrow.down") }.disabled(index == store.partsReviewIDs.count - 1).accessibilityLabel(tr("movePartDown"))
                            }
                            Divider()
                        }
                    }
                }
            }
            Text(tr("partsNoWriteYet")).font(.caption).foregroundStyle(.secondary)
            HStack { Button(tr("cancel")) { store.showingParts = false }.keyboardShortcut(.cancelAction); Spacer(); Button(tr("previewCombinedParts")) { store.confirmPartsPreview() }.buttonStyle(.borderedProminent).disabled(store.partsReviewIDs.count < 2) }
        }.padding(24).frame(width: 760, height: 500)
    }
}
