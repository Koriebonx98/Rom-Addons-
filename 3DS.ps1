# Define the base URL
$baseUrl = "url goes here"

# Get the directory where the script is located
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputFilePath = Join-Path $scriptDir "Nintendo3DS.Games.txt"
$logFilePath = Join-Path $scriptDir "3DS_scrape_log.txt"

# Function to read existing data from the file
function Read-ExistingData {
    if (Test-Path $outputFilePath) {
        return Get-Content $outputFilePath | ForEach-Object {
            if ($_ -match 'Name: "(.*)", URL: "(.*)", Version: "(.*)", Platform: "(.*)"') {
                [PSCustomObject]@{
                    Name     = $matches[1]
                    URL      = $matches[2]
                    Version  = $matches[3]
                    Platform = $matches[4]
                }
            }
        }
    } else {
        return @()
    }
}

# Function to decode URL-encoded strings
function Decode-UrlEncodedString {
    param (
        [string]$urlEncodedString
    )
    [System.Net.WebUtility]::UrlDecode($urlEncodedString)
}

# Function to clean the game name
function Clean-GameName {
    param (
        [string]$name
    )

    # Decode URL-encoded characters and remove version numbers/additional text
    $cleanName = Decode-UrlEncodedString -urlEncodedString $name -replace "\s*\((USA|Europe|Japan|World|En,Fr,De,Es,It,Nl,Sv)\)", ""
    return $cleanName.Trim()
}

# Function to filter out specific non-game URLs
function Is-ValidGameLink {
    param (
        [string]$href
    )

    # Check if the link is a zip file
    return $href -like "*.zip"
}

# Function to write data to the file
function Write-GameToFile {
    param (
        [PSCustomObject]$entry
    )

    if (-not (Test-Path $outputFilePath)) {
        New-Item -Path $outputFilePath -ItemType File -Force | Out-Null
    }

    $line = "Name: ""$($entry.Name)"", URL: ""$($entry.URL)"", Version: ""$($entry.Version)"", Platform: ""Nintendo 3DS"""
    Add-Content -Path $outputFilePath -Value $line
}

# Read existing data
$existingData = Read-ExistingData
$existingDataDictionary = @{}

foreach ($entry in $existingData) {
    $existingDataDictionary[$entry.URL] = $entry
}

# Fetch the HTML content of the page
$html = Invoke-WebRequest -Uri $baseUrl

# Parse the HTML to find all the hyperlinks
$links = $html.Links | Where-Object { Is-ValidGameLink -href $_.href }

# Counters for tracking additions
$addedCount = 0

# Loop through each hyperlink and extract the details
foreach ($link in $links) {
    $href = $link.href
    $name = [System.IO.Path]::GetFileNameWithoutExtension($href)

    $cleanName = Clean-GameName -name $name

    if ($cleanName) {
        $fullUrl = $baseUrl + $href

        $newEntry = [PSCustomObject]@{
            Name    = $cleanName
            URL     = $fullUrl
            Version = "0"  # Default version number
        }

        # Add new entry if it does not already exist
        if (-not $existingDataDictionary.ContainsKey($fullUrl)) {
            $existingDataDictionary[$fullUrl] = $newEntry
            $addedCount++
            Add-Content -Path $logFilePath -Value "[$(Get-Date)] Added ${cleanName}."
            Write-GameToFile -entry $newEntry
        }
    }
}

# Total number of games
$totalGames = $existingDataDictionary.Count

# Log the summary of additions
Add-Content -Path $logFilePath -Value "[$(Get-Date)] Summary: Added $addedCount games. Total number of games: $totalGames."

Write-Output "Scraping complete. Game details saved to $outputFilePath"
