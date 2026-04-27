# Implementation Plan: Settings Window Enhancements

## Overview

This implementation plan covers three independent enhancements to the Settings Window:

1. **Window Drag Support**: Enable dragging the settings window by clicking on the top area
2. **Clear WebDAV Configuration on Logout**: Automatically clear all WebDAV settings when user logs out
3. **Auto-sync WebDAV Configuration on Login**: Automatically fetch and apply WebDAV configuration from cloud after login

The implementation follows the existing patterns in the codebase (AddJsonExtensionWindow for drag support) and integrates with the existing cloud sync infrastructure.

## Tasks

### Feature 1: Settings Window Drag Support

- [x] 1. Implement window drag support in SettingsWindow
  - [x] 1.1 Add MouseLeftButtonDown event handler to top bar Grid in SettingsWindow.xaml
    - Attach `MouseLeftButtonDown="TitleBar_MouseLeftButtonDown"` to the Grid at Row="0"
    - Ensure the event handler is attached to the correct top bar element
    - _Requirements: 1.1, 1.2, 1.3_
  
  - [x] 1.2 Implement IsInteractiveSource helper method in SettingsWindow.xaml.cs
    - Create method to check if click originated from interactive element (Button, TextBox, CheckBox, ComboBox, Slider)
    - Use VisualTreeHelper to traverse parent hierarchy
    - Return true if any parent is an interactive control
    - _Requirements: 1.4_
  
  - [x] 1.3 Verify TitleBar_MouseLeftButtonDown implementation
    - Check if method exists and follows AddJsonExtensionWindow pattern
    - Ensure it calls IsInteractiveSource to prevent drag on interactive elements
    - Ensure it calls DragMove() with InvalidOperationException handling
    - Update implementation if needed to match the design specification
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  
  - [ ]* 1.4 Write unit tests for drag support
    - Test that IsInteractiveSource correctly identifies Button, TextBox, CheckBox
    - Test that IsInteractiveSource returns false for non-interactive elements
    - Test that TitleBar_MouseLeftButtonDown handles InvalidOperationException
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

### Feature 2: Clear WebDAV Configuration on Logout

- [x] 2. Implement WebDAV configuration clearing on logout
  - [x] 2.1 Add Clear method to WebDavCredentialStore class
    - Implement static Clear() method that deletes the credential file
    - Get credential file path using GetCredentialFilePath()
    - Check if file exists before attempting deletion
    - Handle file deletion errors gracefully
    - _Requirements: 2.5_
  
  - [x] 2.2 Implement ClearWebDavConfiguration method in SettingsWindow.xaml.cs
    - Set EnableWebDavSync to false
    - Clear WebDavServerUrl, WebDavRootPath, WebDavUsername to empty strings
    - Clear WebDavPasswordBox.Password
    - Call _mainWindow.SaveWebDavSettings with cleared values
    - Call WebDavCredentialStore.Clear() to remove stored credential
    - Call RefreshWebDavSummary() to update UI
    - Set SyncStatusText to "已退出登录，WebDAV 配置已清除。"
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_
  
  - [x] 2.3 Integrate ClearWebDavConfiguration into SignOutAsync method
    - Add call to ClearWebDavConfiguration() after _mainWindow.SignOutFromSettings()
    - Ensure clearing happens before RefreshAccountSummary()
    - Verify both logout button and menu item trigger this flow
    - _Requirements: 2.8_
  
  - [ ]* 2.4 Write unit tests for WebDAV configuration clearing
    - Test that Clear() method deletes credential file when it exists
    - Test that Clear() method handles missing file gracefully
    - Test that ClearWebDavConfiguration sets all properties to empty/false
    - Test that ClearWebDavConfiguration calls SaveWebDavSettings with cleared values
    - Test that ClearWebDavConfiguration updates UI status text
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

- [ ] 3. Checkpoint - Verify drag support and logout clearing
  - Ensure all tests pass, ask the user if questions arise.

### Feature 3: Auto-sync WebDAV Configuration on Login

- [x] 4. Create WebDAV configuration DTO and cloud client method
  - [x] 4.1 Create WebDavConfigDto class
    - Add class with properties: Enabled, ServerUrl, RootPath, Username, Password
    - Add JsonPropertyName attributes for JSON serialization
    - Place in appropriate namespace (likely with other DTOs)
    - _Requirements: 3.2, 3.3, 3.4, 3.5, 3.6_
  
  - [x] 4.2 Implement FetchWebDavConfigAsync in cloud sync client
    - Load session from SyncSessionStore and validate expiration
    - Create HTTP GET request to /api/sync/webdav-config endpoint
    - Add Bearer token authentication header
    - Handle 404 response (no config on server) by returning null
    - Deserialize JSON response to WebDavConfigDto
    - Handle exceptions and return null on error
    - Add debug logging for success and failure cases
    - _Requirements: 3.1, 3.9_
  
  - [ ]* 4.3 Write unit tests for FetchWebDavConfigAsync
    - Test successful config fetch with valid session
    - Test 404 response returns null without error
    - Test expired session returns null
    - Test network error returns null
    - Test JSON deserialization with valid response
    - _Requirements: 3.1, 3.9, 3.10_

- [x] 5. Implement WebDAV configuration sync in MainWindow
  - [x] 5.1 Implement SyncWebDavConfigFromCloudAsync method in MainWindow.xaml.cs
    - Call _cloudSyncClient.FetchWebDavConfigAsync()
    - If config is null, return early (no config on server)
    - Call SaveWebDavSettings with config values (Enabled, ServerUrl, RootPath, Username)
    - If Password is not empty, call SaveWebDavCredential to store it
    - Call NotifySettingsWindowWebDavConfigChanged() to refresh UI
    - Add debug logging for success
    - Wrap in try-catch and log errors without throwing
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9, 3.10_
  
  - [x] 5.2 Implement NotifySettingsWindowWebDavConfigChanged method in MainWindow.xaml.cs
    - Find open SettingsWindow instance using Application.Current.Windows.OfType<SettingsWindow>()
    - If found, call RefreshWebDavConfigFromExternal() method
    - _Requirements: 3.8_
  
  - [x] 5.3 Integrate SyncWebDavConfigFromCloudAsync into login flow
    - Add call to SyncWebDavConfigFromCloudAsync() in PromptLoginFromSettingsAsync after successful login
    - Place after RefreshCloudFromSettingsAsync() call
    - Ensure it runs only when login succeeds (ok == true)
    - _Requirements: 3.1_
  
  - [ ]* 5.4 Write unit tests for WebDAV sync in MainWindow
    - Test that SyncWebDavConfigFromCloudAsync calls FetchWebDavConfigAsync
    - Test that config values are applied to local settings
    - Test that password is saved to credential store when provided
    - Test that null config (404) doesn't modify local settings
    - Test that exceptions are caught and logged without blocking login
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.9, 3.10_

- [x] 6. Implement UI refresh in SettingsWindow
  - [x] 6.1 Implement RefreshWebDavConfigFromExternal method in SettingsWindow.xaml.cs
    - Reload settings from AppSettingsStore.Load()
    - Update EnableWebDavSync property from loaded settings
    - Update WebDavServerUrl (use default "https://dav.jianguoyun.com/dav/" if empty)
    - Update WebDavRootPath and WebDavUsername from loaded settings
    - Load password from WebDavCredentialStore and set WebDavPasswordBox.Password
    - Call RefreshWebDavSummary() to update UI summary
    - Set SyncStatusText to "WebDAV 配置已从云端同步。"
    - _Requirements: 3.8_
  
  - [ ]* 6.2 Write unit tests for RefreshWebDavConfigFromExternal
    - Test that settings are reloaded from AppSettingsStore
    - Test that UI properties are updated with loaded values
    - Test that password is loaded from credential store
    - Test that default server URL is used when settings value is empty
    - Test that RefreshWebDavSummary is called
    - _Requirements: 3.8_

- [ ] 7. Final checkpoint - Verify all features work together
  - Ensure all tests pass, ask the user if questions arise.

## Integration Testing Notes

After implementation, the following integration tests should be performed manually:

**Window Drag Support**:
- Open Settings Window and verify dragging works on top bar
- Verify dragging does NOT work when clicking on buttons, textboxes, or other controls

**Clear WebDAV Configuration on Logout**:
- Login, configure WebDAV settings, logout, and verify all settings are cleared
- Verify credential file is deleted from disk

**Auto-sync WebDAV Configuration on Login**:
- Ensure WebDAV config exists on server (requires backend implementation)
- Logout, clear local settings, login, and verify settings are synced from cloud
- Test with no config on server to verify local settings remain unchanged
- Verify SettingsWindow UI updates automatically if open during login

## Notes

- Tasks marked with `*` are optional testing tasks and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at logical breakpoints
- All three features are independent and can be implemented in parallel if needed
- Feature 3 requires backend API endpoint implementation (`GET /api/sync/webdav-config`)
- The implementation uses C# with WPF/XAML for the desktop application
