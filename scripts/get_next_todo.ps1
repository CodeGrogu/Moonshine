<#
.SYNOPSIS
    Retrieves the next unblocked actionable task from the Moonshine TODO backlog.
.DESCRIPTION
    Invokes scripts/verify_todo_backlog.ps1 with -Next, optionally formatting output as JSON.
#>

[CmdletBinding()]
param(
    [string]$TodoPath = "TODO.md",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($AsJson) {
    & "$ScriptDir\verify_todo_backlog.ps1" -TodoPath $TodoPath -Next -AsJson
} else {
    & "$ScriptDir\verify_todo_backlog.ps1" -TodoPath $TodoPath -Next
}
