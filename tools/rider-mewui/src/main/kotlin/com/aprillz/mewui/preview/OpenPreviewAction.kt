// Editor context-menu entry: opens the preview tool window for the right-clicked C# file and
// previews that file's project, starting a stopped session or replacing a failed one.
package com.aprillz.mewui.preview

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.wm.ToolWindowManager

class OpenPreviewAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        val extension = event.getData(CommonDataKeys.VIRTUAL_FILE)?.extension
        event.presentation.isEnabledAndVisible =
            event.project != null && extension.equals("cs", ignoreCase = true)
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = event.project ?: return
        val filePath = event.getData(CommonDataKeys.VIRTUAL_FILE)?.path
        val toolWindow = ToolWindowManager.getInstance(project).getToolWindow(PREVIEW_TOOL_WINDOW_ID) ?: return
        toolWindow.activate {
            // Activation creates the content on first use, so the panel is looked up afterwards.
            toolWindow.contentManager.contents
                .firstNotNullOfOrNull { content -> content.component as? PreviewPanel }
                ?.openFor(filePath)
        }
    }
}
