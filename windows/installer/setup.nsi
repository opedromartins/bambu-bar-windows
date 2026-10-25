Unicode true
!include "MUI2.nsh"
Name "Bambu Bar"
OutFile "${OUTDIR}/Bambu-Bar-${VERSION}-windows-x64-setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Bambu Bar"
RequestExecutionLevel user
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\BambuBar.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "PortugueseBR"
!macro CheckRunning
  check_running:
  System::Call 'kernel32::OpenMutexW(i 0x00100000, i 0, w "Local\BambuBar.Windows.Running") p.r0'
  StrCmp $0 0 not_running
  System::Call 'kernel32::CloseHandle(p r0)'
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Bambu Bar is running. Choose Quit in its system tray menu, then Retry.$\r$\nBambu Bar está aberto. Escolha Sair no ícone da bandeja e tente novamente." /SD IDCANCEL IDRETRY check_running
  SetErrorLevel 2
  Abort
  not_running:
!macroend
Function .onInit
  !insertmacro CheckRunning
FunctionEnd
Function un.onInit
  !insertmacro CheckRunning
FunctionEnd
Section "Bambu Bar"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r "${APPDIR}/*"
  CreateDirectory "$SMPROGRAMS\Bambu Bar"
  CreateShortcut "$SMPROGRAMS\Bambu Bar\Bambu Bar.lnk" "$INSTDIR\BambuBar.exe"
  CreateShortcut "$DESKTOP\Bambu Bar.lnk" "$INSTDIR\BambuBar.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "DisplayName" "Bambu Bar"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "DisplayIcon" "$INSTDIR\BambuBar.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar" "NoRepair" 1
SectionEnd
Section "Uninstall"
  SetShellVarContext current
  ExecWait '"$INSTDIR\BambuBar.exe" --uninstall-notifications'
  Delete "$DESKTOP\Bambu Bar.lnk"
  RMDir /r "$SMPROGRAMS\Bambu Bar"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\BambuBar"
SectionEnd
