-- Example Hugin IRC Script
-- This script demonstrates the Lua scripting API

-- Script metadata (optional but recommended)
script_name = "Example Script"
script_version = "1.0.0"
script_author = "Hugin Team"
script_description = "Demonstrates the Lua scripting API for Hugin IRC Server"

-- Called when a user joins a channel
function on_join(event)
    log("User " .. event.nick .. " joined " .. event.channel)
    
    -- Welcome message (uncomment to enable)
    -- irc:SendNotice(event.nick, "Welcome to " .. event.channel .. "!")
    
    return true -- return true to allow, false to cancel
end

-- Called when a user leaves a channel
function on_part(event)
    log("User " .. event.nick .. " left " .. event.channel)
    return true
end

-- Called when a message is sent to a channel
function on_message(event)
    local msg = event.message or ""
    
    -- Example: respond to !hello command
    if msg:lower() == "!hello" then
        irc:SendMessage(event.channel, "Hello, " .. event.nick .. "!")
    end
    
    -- Example: respond to !time command
    if msg:lower() == "!time" then
        local timestamp = irc:Time()
        local formatted = irc:FormatTime(timestamp, "yyyy-MM-dd HH:mm:ss")
        irc:SendMessage(event.channel, "Current time: " .. formatted .. " UTC")
    end
    
    -- Example: respond to !ping command
    if msg:lower() == "!ping" then
        irc:SendMessage(event.channel, "Pong!")
    end
    
    return true -- return false to block the message
end

-- Called when a private message is received
function on_privmsg(event)
    local msg = event.message or ""
    
    -- Example: echo private messages (for testing)
    if msg:lower():match("^!echo ") then
        local echo_msg = msg:sub(7)
        irc:SendMessage(event.nick, "Echo: " .. echo_msg)
    end
    
    return true
end

-- Called when a user changes their nickname
function on_nick(event)
    log("Nick change: " .. event.old_value .. " -> " .. event.new_value)
    return true
end

-- Called when the server starts
function on_server_start(event)
    log("Server started - Example script loaded!")
end

-- Called when a timer fires (if registered)
function on_timer(timer_name)
    log("Timer fired: " .. timer_name)
end

-- You can also define helper functions
function contains(str, pattern)
    return str:find(pattern) ~= nil
end

function starts_with(str, prefix)
    return str:sub(1, #prefix) == prefix
end

-- Log that we're loaded
log("Example script loaded successfully!")
