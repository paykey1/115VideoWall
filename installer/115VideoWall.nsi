Unicode true
RequestExecutionLevel user
!include "MUI2.nsh"
!include "x64.nsh"
!include "LogicLib.nsh"
!include "WinVer.nsh"

!define PRODUCT "115 视频墙"
!define VERSION "1.0.0"
!define KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ME115VideoWall"

Name "${PRODUCT} ${VERSION}"
OutFile "${SETUP_FILE}"
InstallDir "$LOCALAPPDATA\Programs\115VideoWall"
InstallDirRegKey HKCU "${KEY}" "InstallLocation"
SetCompressor /SOLID zlib
VIProductVersion "1.0.0.0"
VIAddVersionKey /LANG=2052 "ProductName" "${PRODUCT}"
VIAddVersionKey /LANG=2052 "FileDescription" "${PRODUCT} 安装程序"
VIAddVersionKey /LANG=2052 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "ME 社 / Massive Electric"
VIAddVersionKey /LANG=2052 "LegalCopyright" "ME 社"

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${MUI_ICON}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\app\115VideoWall.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动 115 视频墙"
!define MUI_FINISHPAGE_RUN_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ${IfNot} ${RunningX64}
  ${OrIfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "115 视频墙需要 64 位 Windows 10 或更高版本。"
    Abort
  ${EndIf}
  SetShellVarContext current
FunctionEnd

Section "安装"
  ${If} ${FileExists} "$INSTDIR\app\*.*"
  ${AndIfNot} ${FileExists} "$INSTDIR\app\.115-video-wall"
    MessageBox MB_ICONSTOP "所选目录已有其他程序，请选择新的安装目录。"
    Abort
  ${EndIf}
  SetOutPath "$INSTDIR\app"
  FileOpen $0 "$INSTDIR\app\.115-video-wall" w
  FileClose $0
  File /r "${PUBLISH_DIR}\*.*"
  nsExec::ExecToStack '"$SYSDIR\icacls.exe" "$INSTDIR\app\WebView2Runtime" /grant "*S-1-15-2-1:(OI)(CI)(RX)" "*S-1-15-2-2:(OI)(CI)(RX)"'
  Pop $0
  Pop $1
  ${If} $0 != "0"
    MessageBox MB_ICONSTOP "无法为内置浏览器设置访问权限，安装未完成。"
    Abort
  ${EndIf}

  WriteUninstaller "$INSTDIR\卸载115视频墙.exe"
  CreateShortCut "$SMPROGRAMS\115 视频墙.lnk" "$INSTDIR\app\115VideoWall.exe" "" "$INSTDIR\app\115VideoWall.exe" 0
  WriteRegStr HKCU "${KEY}" "DisplayName" "${PRODUCT}"
  WriteRegStr HKCU "${KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${KEY}" "Publisher" "ME 社 / Massive Electric"
  WriteRegStr HKCU "${KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${KEY}" "DisplayIcon" "$INSTDIR\app\115VideoWall.exe,0"
  WriteRegStr HKCU "${KEY}" "UninstallString" '$\"$INSTDIR\卸载115视频墙.exe$\"'
  WriteRegDWORD HKCU "${KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\115 视频墙.lnk"
  DeleteRegKey HKCU "${KEY}"
  ${If} ${FileExists} "$INSTDIR\app\.115-video-wall"
    RMDir /r "$INSTDIR\app"
  ${EndIf}
  Delete "$INSTDIR\卸载115视频墙.exe"
  RMDir "$INSTDIR"
SectionEnd
