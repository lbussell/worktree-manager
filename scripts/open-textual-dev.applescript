on run argv
    if (count of argv) > 0 then
        set repoPath to item 1 of argv
    else
        set scriptPath to POSIX path of (path to me)
        set scriptDir to do shell script "dirname " & quoted form of scriptPath
        set repoPath to do shell script "cd " & quoted form of scriptDir & " && cd .. && pwd"
    end if
    set repoPath to do shell script "cd " & quoted form of repoPath & " && pwd"
    
    tell application "Ghostty"
        activate
        
        set cfg to new surface configuration
        set initial working directory of cfg to repoPath
        
        if (count of windows) = 0 then
            set targetWindow to new window with configuration cfg
            set targetTab to selected tab of targetWindow
        else
            set targetWindow to front window
            activate window targetWindow
            set targetTab to new tab in targetWindow with configuration cfg
            select tab targetTab
        end if
        
        set leftTerminal to focused terminal of targetTab
        set rightTerminal to split leftTerminal direction right with configuration cfg
        
        input text "uv run textual console" to rightTerminal
        send key "enter" to rightTerminal
        
        delay 1
        
        input text "uv run textual run --dev src/worktree_fun/main.py" to leftTerminal
        send key "enter" to leftTerminal
        
        focus leftTerminal
    end tell
end run
