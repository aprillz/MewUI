// Tool window wiring: creates the panel, routes session logs to an IDE log category, and
// forwards editor events (document saves for the buildRestart driver, editor selection for
// target auto-match). Preview-side target picks navigate back to the declaring source.
package com.aprillz.mewui.preview

import com.intellij.ide.trustedProjects.TrustedProjects
import com.intellij.ide.util.PropertiesComponent
import com.intellij.openapi.Disposable
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.diagnostic.Logger
import com.intellij.openapi.editor.Document
import com.intellij.openapi.fileEditor.FileDocumentManager
import com.intellij.openapi.fileEditor.FileDocumentManagerListener
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.fileEditor.FileEditorManagerEvent
import com.intellij.openapi.fileEditor.FileEditorManagerListener
import com.intellij.openapi.fileEditor.OpenFileDescriptor
import com.intellij.openapi.project.Project
import com.intellij.openapi.vfs.LocalFileSystem
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory

const val PREVIEW_TOOL_WINDOW_ID = "MewUI Preview"

private const val NAVIGATE_PREFERENCE_KEY = "mewui.preview.navigateOnSelect"

class PreviewToolWindowFactory : ToolWindowFactory {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val log = Logger.getInstance("MewUI.Preview")
        val panel = PreviewPanel(
            logSink = { line, verbose ->
                if (verbose) {
                    log.debug(line)
                } else {
                    log.info(line)
                }
            },
            activeDocumentPathProvider = {
                FileEditorManager.getInstance(project).selectedFiles.firstOrNull()?.path
            },
            solutionDirectoryProvider = { project.basePath },
            navigateToSource = { path, line ->
                ApplicationManager.getApplication().invokeLater {
                    if (!project.isDisposed) {
                        val file = LocalFileSystem.getInstance().refreshAndFindFileByPath(path.replace('\\', '/'))
                        if (file != null) {
                            // Without focus: keyboard input stays in the preview the user is working in.
                            OpenFileDescriptor(project, file, ((line ?: 1) - 1).coerceAtLeast(0), 0).navigate(false)
                        }
                    }
                }
            },
            isProjectTrusted = { TrustedProjects.isProjectTrusted(project) },
            dotnetHostProvider = DotnetLocator::resolve,
            loadNavigatePreference = {
                PropertiesComponent.getInstance().getBoolean(NAVIGATE_PREFERENCE_KEY, true)
            },
            saveNavigatePreference = { value ->
                PropertiesComponent.getInstance().setValue(NAVIGATE_PREFERENCE_KEY, value, true)
            },
        )

        val content = ContentFactory.getInstance().createContent(panel, "", false)
        content.setDisposer(Disposable { panel.stopSession() })
        toolWindow.contentManager.addContent(content)

        val connection = project.messageBus.connect(content)
        connection.subscribe(FileDocumentManagerListener.TOPIC, object : FileDocumentManagerListener {
            override fun beforeDocumentSaving(document: Document) {
                FileDocumentManager.getInstance().getFile(document)?.let { file ->
                    panel.notifySourceChanged(file.path)
                }
            }
        })
        connection.subscribe(FileEditorManagerListener.FILE_EDITOR_MANAGER, object : FileEditorManagerListener {
            override fun selectionChanged(event: FileEditorManagerEvent) {
                panel.autoMatchTarget(event.newFile?.path, fromEditor = true)
            }
        })

        // Opening the tool window states the intent to preview: start without a manual Start
        // (the panel refuses for an untrusted project).
        panel.autoStart()
    }
}
