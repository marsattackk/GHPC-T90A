# Checks that the settings tables in README.md and MAINTAINING.md still match the code. It finds every CreateEntry call
# in T90A\ and Common\, works out each setting's name and default (a literal, a const, or the argument a single caller
# passes), and then checks:
#   README.md       one row per setting that isn't hidden, with the same default
#   MAINTAINING.md  one row naming every setting, hidden ones too, and "Defined in" naming the file the entry is made in
#                   every file that column names exists and mentions the setting (a note says when a setting made in Common\ is set up by a tank file the row does not name)
# The descriptions aren't compared. The code's are the cfg file's wording, the README's are written for players.
#
#   powershell -ExecutionPolicy Bypass -File tools\check-settings.ps1
#
# Exit code 0: they agree. 1: something differs, each difference on its own line.
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$sources = @(Get-ChildItem (Join-Path $repo "T90A"), (Join-Path $repo "Common") -Filter *.cs -Recurse)
$texts = @{}
foreach ($file in $sources) { $texts[$file.FullName.Substring($repo.Length + 1)] = [IO.File]::ReadAllText($file.FullName) }
$problems = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]   # worth a look, not a failure

# The arguments of the call whose "(" is at $open, split at top-level commas, and where the call ends.
function Get-Arguments([string]$text, [int]$open) {
	$arguments = New-Object System.Collections.Generic.List[string]
	$depth = 0
	$start = $open + 1
	$i = $open
	while ($i -lt $text.Length) {
		$c = $text[$i]
		if ($c -eq '"') {
			$i++
			while ($i -lt $text.Length -and $text[$i] -ne '"') { if ($text[$i] -eq '\') { $i++ }; $i++ }
		}
		elseif ($c -eq '(' -or $c -eq '[' -or $c -eq '{') { $depth++ }
		elseif ($c -eq ')' -or $c -eq ']' -or $c -eq '}') {
			$depth--
			if ($depth -eq 0) {
				$arguments.Add($text.Substring($start, $i - $start).Trim())
				return [pscustomobject]@{ Arguments = $arguments.ToArray(); End = $i }
			}
		}
		elseif ($c -eq ',' -and $depth -eq 1) {
			$arguments.Add($text.Substring($start, $i - $start).Trim())
			$start = $i + 1
		}
		$i++
	}
	throw "A call that never closes."
}

# Every const by its bare name. A name declared twice with different values can't be told apart.
$consts = @{}
foreach ($path in $texts.Keys) {
	foreach ($declaration in [regex]::Matches($texts[$path], 'const\s+\w+\s+([^;]+);')) {
		foreach ($part in [regex]::Matches($declaration.Groups[1].Value, '(\w+)\s*=\s*("[^"]*"|[^,]+)')) {
			$name = $part.Groups[1].Value
			$value = $part.Groups[2].Value.Trim()
			if ($consts.ContainsKey($name) -and $consts[$name] -ne $value) { $consts[$name] = $null }
			else { $consts[$name] = $value }
		}
	}
}

# A name or default as written: a literal, a const ("NightSight.Essa"), or a parameter of the method around the call
# that exactly one caller fills.
function Resolve-Value([string]$expression, [string]$path, [int]$at) {
	if ($expression -match '^(.+)\.ToString\(\)$') {
		$inner = Resolve-Value $Matches[1] $path $at
		if ($inner -match '^\w+\.(\w+)$') { return '"' + $Matches[1] + '"' }
		return $inner
	}
	if ($expression -match '^(".*"|true|false|-?[\d.]+[fFdDmM]?)$') { return $expression }
	$bare = ($expression -split '\.')[-1]
	if ($consts.ContainsKey($bare)) {
		if ($null -eq $consts[$bare]) { return $null }
		return $consts[$bare]
	}
	$before = $texts[$path].Substring(0, $at)
	$method = [regex]::Matches($before, 'static\s+[\w<>.,\[\] ]+?\s+(\w+)\s*\(([^)]*)\)') | Select-Object -Last 1
	$class = [regex]::Matches($before, '\bclass\s+(\w+)') | Select-Object -Last 1
	if (-not $method -or -not $class) { return $null }
	$parameters = @($method.Groups[2].Value -split ',' | ForEach-Object { (($_.Trim()) -split '\s+')[-1] })
	$index = [array]::IndexOf($parameters, $expression)
	if ($index -lt 0) { return $null }
	$calls = @()
	foreach ($callerPath in $texts.Keys) {
		$callerText = $texts[$callerPath]
		foreach ($call in [regex]::Matches($callerText, '\b' + $class.Groups[1].Value + '\.' + $method.Groups[1].Value + '\s*\(')) {
			$calls += , (Get-Arguments $callerText ($call.Index + $call.Length - 1)).Arguments
		}
	}
	if ($calls.Count -ne 1 -or $calls[0].Count -le $index) { return $null }
	return Resolve-Value $calls[0][$index] $path $at
}

function Normalize([string]$value) {
	$value = $value.Trim().Trim('`').Trim('"')
	if ($value -match '^-?[\d.]+[fFdDmM]$') { $value = $value.Substring(0, $value.Length - 1) }
	return $value.ToLowerInvariant()
}

$settings = [ordered]@{}
foreach ($path in $texts.Keys) {
	$text = $texts[$path]
	foreach ($call in [regex]::Matches($text, '\.CreateEntry\s*\(')) {
		$arguments = (Get-Arguments $text ($call.Index + $call.Length - 1)).Arguments
		$name = Resolve-Value $arguments[0] $path $call.Index
		$default = Resolve-Value $arguments[1] $path $call.Index
		if (-not $name) {
			$problems.Add("${path}: can't tell the name of the setting made with $($arguments[0]).")
			continue
		}
		$name = $name.Trim('"')
		if ($null -eq $default) { $problems.Add("${path}: can't tell the default of `"$name`" ($($arguments[1])).") }
		$hidden = [bool]($arguments | Where-Object { $_ -match '^is_hidden\s*:\s*true$' })
		# The class the entry is made in: when that is shared code in Common\, the tank file that calls it sets the setting up.
		$made = [regex]::Matches($text.Substring(0, $call.Index), '\bclass\s+(\w+)') | Select-Object -Last 1
		$settings[$name] = [pscustomobject]@{ Default = $default; Hidden = $hidden; Path = $path; Class = $(if ($made) { $made.Groups[1].Value } else { $null }) }
	}
}

# A setting made in shared code (Common\) is this tank's only when a tank file uses the class that makes it: Common\ is the
# same files in every repo, and a tank without, say, armour presets never creates "Armour preset".
$tankText = ($texts.Keys | Where-Object { $_ -notlike 'Common\*' } | ForEach-Object { $texts[$_] }) -join "`n"
foreach ($name in @($settings.Keys)) {
	$setting = $settings[$name]
	if ($setting.Path -notlike 'Common\*') { continue }
	# Any class the file declares (the entry may sit under a nested one, like UpdateCheck's Result), matched case for case: a
	# nested class "Pair" is not the word "pair" in a comment.
	$classes = [regex]::Matches($texts[$setting.Path], '\bclass\s+(\w+)') | ForEach-Object { $_.Groups[1].Value }
	if ($classes -and -not ($classes | Where-Object { $tankText -cmatch "\b$([regex]::Escape($_))\b" })) { $settings.Remove($name) }
}

# The rows of the Markdown table under the header line that starts with $header, as arrays of cells.
function Get-Rows([string]$file, [string]$header) {
	$lines = [IO.File]::ReadAllLines((Join-Path $repo $file))
	$start = [array]::FindIndex($lines, [Predicate[string]] { param($line) $line.StartsWith($header) })
	if ($start -lt 0) { throw "No table starting `"$header`" in $file." }
	$rows = @()
	for ($i = $start + 2; $i -lt $lines.Length -and $lines[$i].StartsWith("|"); $i++) {
		$rows += , @($lines[$i].Trim().Trim('|') -split ' \| ' | ForEach-Object { $_.Trim() })
	}
	return $rows
}

$readme = @{}
foreach ($row in Get-Rows "README.md" "| Setting | Values | Default |") { $readme[$row[0].Trim('`')] = $row[2] }
foreach ($name in $settings.Keys) {
	$setting = $settings[$name]
	if ($setting.Hidden) {
		if ($readme.ContainsKey($name)) { $problems.Add("README.md: `"$name`" is hidden, it has no row in the settings table.") }
		continue
	}
	if (-not $readme.ContainsKey($name)) { $problems.Add("README.md: no row for `"$name`" (made in $($setting.Path)).") }
	elseif ($null -ne $setting.Default -and (Normalize $readme[$name]) -ne (Normalize $setting.Default)) {
		$problems.Add("README.md: `"$name`" says default $($readme[$name]), the code has $($setting.Default) ($($setting.Path)).")
	}
}
foreach ($name in $readme.Keys) { if (-not $settings.Contains($name)) { $problems.Add("README.md: `"$name`" isn't a setting in the code.") } }

$maintaining = @{}
foreach ($row in Get-Rows "MAINTAINING.md" "| Setting | Defined in | Read by |") {
	foreach ($named in [regex]::Matches($row[0], '`([^`]+)`')) { $maintaining[$named.Groups[1].Value] = $row[1] }
}
foreach ($name in $settings.Keys) {
	$path = $settings[$name].Path
	$short = $path -replace '^T90A\\', ''
	if (-not $maintaining.ContainsKey($name)) { $problems.Add("MAINTAINING.md: no row for `"$name`" (made in $path).") }
	elseif ($maintaining[$name] -notmatch [regex]::Escape("``$path``") -and $maintaining[$name] -notmatch [regex]::Escape("``$short``")) {
		$problems.Add("MAINTAINING.md: `"$name`" is made in $path, the table says $($maintaining[$name]).")
	}
	if ($maintaining.ContainsKey($name)) {
		# Every file the row names exists (T90A\ may be left off) and mentions the setting: its name, or the class that makes the entry.
		$class = $settings[$name].Class
		$files = @([regex]::Matches($maintaining[$name], '`([^`]+\.cs)`') | ForEach-Object { $_.Groups[1].Value })
		$resolved = @()
		foreach ($file in $files) {
			$key = if ($texts.ContainsKey($file)) { $file } elseif ($texts.ContainsKey("T90A\$file")) { "T90A\$file" } else { $null }
			if (-not $key) { $problems.Add("MAINTAINING.md: `"$name`" names $file, which isn't a source file of this repo."); continue }
			$resolved += $key
			# "Armour preset" is mentioned by the string, by a name like ArmourPreset (its text in SettingText), or by the class that makes the entry.
			$flat = ($texts[$key] -replace '[^A-Za-z0-9]', '').ToLowerInvariant()
			$mentions = $flat.Contains((($name -replace '[^A-Za-z0-9]', '').ToLowerInvariant())) -or ($class -and $texts[$key] -cmatch ('\b' + [regex]::Escape($class) + '\b'))
			if (-not $mentions) { $problems.Add("MAINTAINING.md: `"$name`" names $file, which doesn't mention the setting or $class.") }
		}
		# A setting made in shared code (Common\) is set up by a tank file: said as a note, not a failure.
		if ($path -like 'Common\*' -and $class) {
			$callers = @($texts.Keys | Where-Object { $_ -like 'T90A\*' -and $texts[$_] -cmatch ('\b' + [regex]::Escape($class) + '\.') })
			if ($callers.Count -gt 0 -and -not ($resolved | Where-Object { $callers -contains $_ })) {
				$notes.Add("MAINTAINING.md: `"$name`" is made in $path ($class) and set up in $($callers -join ', '); the row names only $($files -join ', ').")
			}
		}
	}
}
foreach ($name in $maintaining.Keys) { if (-not $settings.Contains($name)) { $problems.Add("MAINTAINING.md: `"$name`" isn't a setting in the code.") } }

$notes | ForEach-Object { Write-Host "note: $_" }
if ($problems.Count -gt 0) {
	$problems | ForEach-Object { Write-Host $_ }
	exit 1
}
Write-Host "$($settings.Count) settings: README.md and MAINTAINING.md match the code."
exit 0
