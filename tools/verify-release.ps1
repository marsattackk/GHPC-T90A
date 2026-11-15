<#
.SYNOPSIS
Checks that a release DLL of T90A is what this repo's tag builds: builds the tag in a temporary clone as MAINTAINING.md
"Releasing" says and compares the bytes.

.DESCRIPTION
  tools\verify-release.ps1 -Tag v1.0.0 -Release C:\path\T90A.dll -GameDir "<GHPC folder>\Bin"
  tools\verify-release.ps1 -Tag v1.0.0 -Release C:\path\T90A_1.0.0.zip -GameDir "<GHPC folder>\Bin"

-Release is the DLL, or the release zip (Mods\T90A.dll is taken out of it). The tag is cloned into a temporary folder (this
repo is not touched) and built with `dotnet build -c Release -p:GameDir=<GameDir> -p:GitCommit=<commit>`; the commit is the
one the release DLL's own version stamp carries (it is read from the DLL, "<version>+<commit7>[.dirty].<dev|public>"), because
the stamp goes into the DLL and a deterministic build only repeats byte for byte with the same stamp. When the stamp's commit
is not the tag's commit that is said; the build is the tag's source either way. A DLL you built yourself without
-p:GitCommit says "+unknown" in its stamp and names no commit: pass -Commit <short hash> (the tag's own, for a build of the tag).

Says IDENTICAL, or what differs: sizes, the number of differing bytes and where the first one is. Exit code 0: identical,
1: different, 2: the check could not be made (no tag, no DLL, the build failed). The game's files are only read (GameDir is
the reference folder the build needs); nothing is written outside the temporary folder.
Needs: git and the .NET SDK on the PATH, Windows PowerShell 5.1 or newer.
#>
param(
	[Parameter(Mandatory = $true)][string]$Tag,
	[Parameter(Mandatory = $true)][string]$Release,
	[string]$GameDir = $env:GHPC_GAME_DIR,
	[string]$Repo = "",
	[string]$Commit = ""
)
# Not a parameter default: under `powershell -File` $PSScriptRoot is empty while the defaults are worked out (Windows PowerShell 5.1).
if (-not $Repo) { $Repo = Split-Path -Parent $PSScriptRoot }
$ErrorActionPreference = 'Continue'   # git and dotnet report through their exit codes, checked below

function Fail([string]$message) { Write-Host $message; exit 2 }

if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'GHPC_Data\Managed\Assembly-CSharp.dll'))) {
	Fail "GameDir: pass -GameDir '<GHPC folder>\Bin' (the folder with GHPC_Data and MelonLoader), or set GHPC_GAME_DIR."
}
if (-not (Test-Path $Release)) { Fail "Release: '$Release' not found." }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("verify-release-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
try {
	# the release DLL, out of the zip when it is one
	$dll = $Release
	if ($Release -like '*.zip') {
		Add-Type -AssemblyName System.IO.Compression.FileSystem
		$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Release))
		try {
			$entry = $zip.Entries | Where-Object { $_.FullName -match '^Mods[\\/][^\\/]+\.dll$' } | Select-Object -First 1
			if (-not $entry) { Fail "Release: no Mods\<name>.dll in the zip." }
			$dll = Join-Path $work 'release.dll'
			[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dll)
		}
		finally { $zip.Dispose() }
	}
	$releaseBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $dll))

	# the stamp the DLL carries: <version>+<commit7>[.dirty].<dev|public>
	$text = [System.Text.Encoding]::GetEncoding(28591).GetString($releaseBytes)
	$stamp = [regex]::Match($text, '(\d+\.\d+\.\d+[0-9A-Za-z.\-]*)\+([0-9a-f]{7})(\.dirty)?\.(dev|public)')
	if (-not $stamp.Success) {
		$unknown = [regex]::Match($text, '(\d+\.\d+\.\d+[0-9A-Za-z.\-]*)\+unknown\.(dev|public)')
		if ($unknown.Success -and $Commit) {
			$stamp = $unknown
			$stampCommit = $Commit
		}
		elseif ($unknown.Success) {
			Fail "Release: the DLL's stamp says '$($unknown.Value)': it was built without -p:GitCommit, so it does not name the commit it came from. Say which commit to build with: -Commit <the commit's short hash> (the tag's own is fine for a release you built from the tag), or build the release with -p:GitCommit=<commit> as MAINTAINING.md 'Releasing' says."
		}
		else { Fail "Release: no version stamp ('<version>+<commit>.<dev|public>') in the DLL; is it a build of this repo?" }
	}
	else {
		if ($stamp.Groups[3].Success) { Write-Host "Note: the release was built from a dirty tree ('$($stamp.Value)'); an identical rebuild is not expected." }
		$stampCommit = if ($Commit) { $Commit } else { $stamp.Groups[2].Value }
	}

	# the tag, cloned
	$clone = Join-Path $work 'src'
	git clone --quiet --no-hardlinks "$Repo" "$clone" 2>&1 | Out-Null
	if ($LASTEXITCODE -ne 0) { Fail "git clone of '$Repo' failed." }
	git -C "$clone" checkout --quiet "$Tag" 2>&1 | Out-Null
	if ($LASTEXITCODE -ne 0) { Fail "Tag '$Tag' not found in '$Repo'." }
	$tagCommit = (git -C "$clone" rev-parse --short=7 HEAD).Trim()
	Write-Host "Tag $Tag is commit $tagCommit; the release DLL's stamp is '$($stamp.Value)'."
	if ($tagCommit -ne $stampCommit) {
		Write-Host "The stamp's commit ($stampCommit) is not the tag's ($tagCommit): the release was stamped with the commit GitHub gave it. The build uses the stamp's."
	}

	# MAINTAINING.md "Releasing": dotnet build -c Release -p:GameDir=... -p:GitCommit=<commit>
	$project = Get-ChildItem -Path $clone -Filter '*.csproj' -File | Select-Object -First 1
	if (-not $project) { Fail "No project file at the top of the tag." }
	dotnet build $project.FullName -c Release "-p:GameDir=$GameDir" "-p:GitCommit=$stampCommit" -nologo -v q 2>&1 | Out-Null
	if ($LASTEXITCODE -ne 0) { Fail "The build of tag $Tag failed (run the same dotnet build by hand to see why)." }
	$built = Get-ChildItem -Path $clone -Recurse -Filter ($project.BaseName + '.dll') -File |
		Where-Object { $_.FullName -match '\\bin\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
	if (-not $built) { Fail "The build made no $($project.BaseName).dll." }
	$builtBytes = [System.IO.File]::ReadAllBytes($built.FullName)

	if ($builtBytes.Length -eq $releaseBytes.Length) {
		$differing = 0
		$first = -1
		for ($i = 0; $i -lt $builtBytes.Length; $i++) {
			if ($builtBytes[$i] -ne $releaseBytes[$i]) { $differing++; if ($first -lt 0) { $first = $i } }
		}
		if ($differing -eq 0) { Write-Host "IDENTICAL: the DLL built from $Tag is the release DLL ($($builtBytes.Length) bytes)."; exit 0 }
		Write-Host ("DIFFERENT: same size ($($builtBytes.Length) bytes), $differing byte(s) differ, the first at offset 0x{0:x}." -f $first)
	}
	else {
		Write-Host "DIFFERENT: the build is $($builtBytes.Length) bytes, the release DLL $($releaseBytes.Length)."
	}
	Write-Host "Likely causes: another game folder (the build embeds nothing from it, but a different compiler or SDK can change the bytes), a release built from other source than the tag, or a different stamp. Compare the decompiled code (ilspycmd) to see whether the code differs."
	exit 1
}
finally {
	Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
