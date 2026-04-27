# Requirements Document

## Introduction

This document specifies requirements for three enhancements to the Settings Window in the Yanzi (Swallow) application:

1. **Window Drag Support**: Enable users to drag the settings window by clicking on the top area
2. **Clear WebDAV Configuration on Logout**: Automatically clear WebDAV settings when user logs out from cloud account
3. **Auto-sync WebDAV Configuration on Login**: Automatically fetch and apply WebDAV configuration from cloud after successful login

These enhancements improve user experience by providing more intuitive window interaction and better synchronization of WebDAV settings with the cloud account.

## Glossary

- **Settings_Window**: The main settings dialog window (SettingsWindow.xaml/SettingsWindow.xaml.cs)
- **WebDAV_Configuration**: The set of WebDAV synchronization settings including EnableWebDavSync, WebDavServerUrl, WebDavRootPath, WebDavUsername, and stored password credential
- **Cloud_Account**: The user's cloud synchronization account managed by the cloud sync client
- **Top_Area**: The non-interactive region at the top of the Settings Window including the title bar and header section
- **Drag_Support**: The ability to move a window by clicking and dragging on a designated area
- **Logout_Operation**: The action triggered when user clicks "退出登录" (Sign Out) button or menu item
- **Login_Operation**: The successful authentication and session establishment with the cloud account
- **WebDAV_Credential_Store**: The secure storage mechanism for WebDAV passwords using Windows DPAPI
- **Main_Window**: The primary application window (MainWindow.xaml.cs) that manages cloud sync operations

## Requirements

### Requirement 1: Settings Window Drag Support

**User Story:** As a user, I want to drag the settings window by clicking on the top area, so that I can reposition the window more intuitively without needing to target the window title bar.

#### Acceptance Criteria

1. WHEN the user clicks and holds the left mouse button on THE Top_Area, THE Settings_Window SHALL initiate a drag operation
2. WHILE the user drags with the left mouse button held down, THE Settings_Window SHALL follow the mouse cursor position
3. WHEN the user releases the left mouse button, THE Settings_Window SHALL stop at the current position
4. IF the user clicks on an interactive element (button, textbox, checkbox) in THE Top_Area, THEN THE Settings_Window SHALL NOT initiate a drag operation
5. THE Settings_Window SHALL use the same drag implementation pattern as AddJsonExtensionWindow (MouseLeftButtonDown event handler calling DragMove())

### Requirement 2: Clear WebDAV Configuration on Logout

**User Story:** As a user, I want my WebDAV configuration to be cleared when I log out from my cloud account, so that my personal sync settings are not retained when switching accounts or logging out.

#### Acceptance Criteria

1. WHEN the user triggers THE Logout_Operation, THE Settings_Window SHALL clear the EnableWebDavSync setting to false
2. WHEN the user triggers THE Logout_Operation, THE Settings_Window SHALL clear the WebDavServerUrl field
3. WHEN the user triggers THE Logout_Operation, THE Settings_Window SHALL clear the WebDavRootPath field
4. WHEN the user triggers THE Logout_Operation, THE Settings_Window SHALL clear the WebDavUsername field
5. WHEN the user triggers THE Logout_Operation, THE Settings_Window SHALL remove the stored password credential from THE WebDAV_Credential_Store
6. WHEN THE WebDAV_Configuration is cleared, THE Settings_Window SHALL update the UI to reflect the cleared state
7. WHEN THE WebDAV_Configuration is cleared, THE Settings_Window SHALL save the updated settings to persistent storage
8. THE Settings_Window SHALL perform WebDAV configuration clearing for both logout menu item and logout button actions

### Requirement 3: Auto-sync WebDAV Configuration on Login

**User Story:** As a user, I want my WebDAV configuration to be automatically synced from the cloud when I log in, so that I don't have to manually configure WebDAV settings on each device.

#### Acceptance Criteria

1. WHEN THE Login_Operation completes successfully, THE Main_Window SHALL fetch WebDAV configuration from the cloud server
2. IF the cloud server returns WebDAV configuration data, THEN THE Main_Window SHALL apply the EnableWebDavSync setting to local settings
3. IF the cloud server returns WebDAV configuration data, THEN THE Main_Window SHALL apply the WebDavServerUrl to local settings
4. IF the cloud server returns WebDAV configuration data, THEN THE Main_Window SHALL apply the WebDavRootPath to local settings
5. IF the cloud server returns WebDAV configuration data, THEN THE Main_Window SHALL apply the WebDavUsername to local settings
6. IF the cloud server returns WebDAV password credential, THEN THE Main_Window SHALL store it in THE WebDAV_Credential_Store
7. WHEN WebDAV configuration is synced from cloud, THE Main_Window SHALL save the updated settings to persistent storage
8. IF THE Settings_Window is open during login, THEN THE Settings_Window SHALL refresh its UI to display the synced WebDAV configuration
9. IF the cloud server does not have WebDAV configuration, THEN THE Main_Window SHALL NOT modify existing local WebDAV settings
10. THE Main_Window SHALL log any errors during WebDAV configuration sync without blocking the login process
