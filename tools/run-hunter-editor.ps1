param([switch]$Quick,[string]$Tuning,[switch]$SkinDebug)
$ErrorActionPreference='Stop'
$hunterRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if(Get-Process Unity -ErrorAction SilentlyContinue){throw 'Another Unity Editor owns the coordinated slot.'}
$hunterSource=(& git -C $hunterRoot rev-parse HEAD).Trim()
if(& git -C $hunterRoot status --porcelain){throw 'Commit source before recording Editor runtime evidence.'}
$hunterScene='Assets/CityLife/GeneratedPreview-Character-KookerStarfallHunter-0.0.3-preview.1-20260914-060213/FirstInhabitant.unity'
$hunterStamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$hunterEvidence=Join-Path $hunterRoot ('evidence/local/hunter/editor-'+$hunterStamp)
$null=New-Item -ItemType Directory -Path $hunterEvidence
$hunterArgs='-projectPath "'+$hunterRoot+'" -executeMethod CityLife.World.Editor.HunterPlayMode.Run -hunterEditorScene "'+$hunterScene+'" -hunterGripVerify -hunterEvidence "'+$hunterEvidence+'" -logFile "'+(Join-Path $hunterEvidence 'editor.log')+'"'
if($SkinDebug){$hunterArgs+=' -hunterSkinDebug'}
if($Quick){$hunterArgs+=' -hunterGripQuick'}
if($Tuning){$hunterArgs+=' -hunterGripTuning "'+(Resolve-Path -LiteralPath $Tuning).Path+'"'}
$hunterSettings=@{}
foreach($hunterName in @('GraphicsSettings.asset','QualitySettings.asset','ProjectSettings.asset')){
 $hunterPath=Join-Path $hunterRoot ('ProjectSettings/'+$hunterName);$hunterSettings[$hunterPath]=[IO.File]::ReadAllBytes($hunterPath)
}
try{
 $hunterProcess=Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -ArgumentList $hunterArgs -WindowStyle Normal -PassThru
 $hunterProcess.WaitForExit()
 [ordered]@{scope='Unity Editor Play Mode only; not standalone acceptance';sourceCommit=$hunterSource;scene=$hunterScene;quick=[bool]$Quick;exitCode=$hunterProcess.ExitCode;utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $hunterEvidence 'editor-session.json')
 if($hunterProcess.ExitCode -ne 0){throw ('Editor failed: '+$hunterEvidence)}
}finally{foreach($hunterPath in $hunterSettings.Keys){[IO.File]::WriteAllBytes($hunterPath,$hunterSettings[$hunterPath])}}
Write-Output ('EDITOR_RUNTIME_EVIDENCE: '+$hunterEvidence)
