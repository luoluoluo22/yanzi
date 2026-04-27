# Bugfix Requirements Document

## Introduction

This bugfix addresses three remaining issues with the WebDAV password integration in the settings window after the password input was moved from a separate dialog to the main settings UI. The issues affect the status display accuracy, password clearing functionality, and code cleanliness.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the user types a password in the WebDavPasswordBox but has not clicked "保存配置" THEN the system displays "未设置密码" status even though the password box contains text

1.2 WHEN the user clears the password box and clicks "保存配置" THEN the system does not remove the stored credential from WebDavCredentialStore

1.3 WHEN the codebase is examined THEN the system contains the unused method SetWebDavCredentialButton_Click that is no longer called since the "设置密码" button was removed

### Expected Behavior (Correct)

2.1 WHEN the user types a password in the WebDavPasswordBox (regardless of whether it's saved) THEN the system SHALL display a status that reflects the current password box state (e.g., "已配置" if password box has content)

2.2 WHEN the user clears the password box and clicks "保存配置" THEN the system SHALL remove the stored credential from WebDavCredentialStore using WebDavCredentialStore.Clear()

2.3 WHEN the codebase is examined THEN the system SHALL NOT contain the unused SetWebDavCredentialButton_Click method

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the user enters a non-empty password and clicks "保存配置" THEN the system SHALL CONTINUE TO save the password using _mainWindow.SaveWebDavCredential()

3.2 WHEN the settings window is activated THEN the system SHALL CONTINUE TO load the saved password from WebDavCredentialStore into the WebDavPasswordBox

3.3 WHEN the user changes the password in the password box THEN the system SHALL CONTINUE TO call RefreshWebDavSummary() to update the status display

3.4 WHEN EnableWebDavSync is false THEN the system SHALL CONTINUE TO display "未启用个人扩展同步。" regardless of password state
