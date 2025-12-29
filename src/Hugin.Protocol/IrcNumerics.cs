using System.Globalization;
using Hugin.Core.Enums;

namespace Hugin.Protocol;

/// <summary>
/// Provides methods for constructing IRC numeric replies.
/// </summary>
public static class IrcNumerics
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Creates a numeric reply message.
    /// </summary>
    public static IrcMessage CreateNumeric(
        string serverName,
        NumericReply numeric,
        string target,
        params string[] args)
    {
        var numericStr = ((int)numeric).ToString("D3", Inv);
        var parameters = new List<string>(args.Length + 1) { target };
        parameters.AddRange(args);
        return IrcMessage.CreateWithSource(serverName, numericStr, parameters.ToArray());
    }

    #region Connection Registration Numerics (001-005)

    /// <summary>Creates RPL_WELCOME (001) - Welcome message after successful registration.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="fullHost">User's full hostmask (nick!user@host).</param>
    public static IrcMessage Welcome(string server, string nick, string fullHost) =>
        CreateNumeric(server, NumericReply.RplWelcome, nick,
            $"Welcome to the Internet Relay Network {fullHost}");

    /// <summary>Creates RPL_YOURHOST (002) - Server host and version information.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="serverName">The server hostname.</param>
    /// <param name="version">Server software version.</param>
    public static IrcMessage YourHost(string server, string nick, string serverName, string version) =>
        CreateNumeric(server, NumericReply.RplYourHost, nick,
            $"Your host is {serverName}, running version {version}");

    /// <summary>Creates RPL_CREATED (003) - Server creation date.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="created">When the server was started.</param>
    public static IrcMessage Created(string server, string nick, DateTimeOffset created) =>
        CreateNumeric(server, NumericReply.RplCreated, nick,
            $"This server was created {created:ddd MMM dd yyyy HH:mm:ss} UTC");

    /// <summary>Creates RPL_MYINFO (004) - Server info with supported modes.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="serverName">The server hostname.</param>
    /// <param name="version">Server software version.</param>
    /// <param name="userModes">Available user modes.</param>
    /// <param name="channelModes">Available channel modes.</param>
    public static IrcMessage MyInfo(string server, string nick, string serverName, string version,
        string userModes, string channelModes) =>
        CreateNumeric(server, NumericReply.RplMyInfo, nick,
            serverName, version, userModes, channelModes);

    /// <summary>Creates RPL_ISUPPORT (005) - Server capability tokens.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="tokens">ISUPPORT tokens (e.g., "CHANTYPES=#", "PREFIX=(ov)@+").</param>
    public static IrcMessage ISupport(string server, string nick, params string[] tokens) =>
        CreateNumeric(server, NumericReply.RplISupport, nick,
            tokens.Append("are supported by this server").ToArray());

    #endregion

    #region Lusers Numerics (251-255, 265-266)

    /// <summary>Creates RPL_LUSERCLIENT (251) - Total users and servers.</summary>
    public static IrcMessage LuserClient(string server, string nick, int users, int invisible, int servers) =>
        CreateNumeric(server, NumericReply.RplLUserClient, nick,
            $"There are {users} users and {invisible} invisible on {servers} servers");

    /// <summary>Creates RPL_LUSEROP (252) - Number of IRC operators online.</summary>
    public static IrcMessage LuserOp(string server, string nick, int operators) =>
        CreateNumeric(server, NumericReply.RplLUserOp, nick,
            operators.ToString(Inv), "operator(s) online");

    /// <summary>Creates RPL_LUSERUNKNOWN (253) - Number of unknown connections.</summary>
    public static IrcMessage LuserUnknown(string server, string nick, int unknown) =>
        CreateNumeric(server, NumericReply.RplLUserUnknown, nick,
            unknown.ToString(Inv), "unknown connection(s)");

    /// <summary>Creates RPL_LUSERCHANNELS (254) - Number of channels formed.</summary>
    public static IrcMessage LuserChannels(string server, string nick, int channels) =>
        CreateNumeric(server, NumericReply.RplLUserChannels, nick,
            channels.ToString(Inv), "channels formed");

    /// <summary>Creates RPL_LUSERME (255) - Local server client count.</summary>
    public static IrcMessage LuserMe(string server, string nick, int clients, int servers) =>
        CreateNumeric(server, NumericReply.RplLUserMe, nick,
            $"I have {clients} clients and {servers} servers");

    /// <summary>Creates RPL_LOCALUSERS (265) - Local user statistics.</summary>
    public static IrcMessage LocalUsers(string server, string nick, int current, int max) =>
        CreateNumeric(server, NumericReply.RplLocalUsers, nick,
            current.ToString(Inv), max.ToString(Inv),
            $"Current local users {current}, max {max}");

    /// <summary>Creates RPL_GLOBALUSERS (266) - Global user statistics.</summary>
    public static IrcMessage GlobalUsers(string server, string nick, int current, int max) =>
        CreateNumeric(server, NumericReply.RplGlobalUsers, nick,
            current.ToString(Inv), max.ToString(Inv),
            $"Current global users {current}, max {max}");

    #endregion

    #region MOTD Numerics (372, 375, 376, 422)

    /// <summary>Creates RPL_MOTDSTART (375) - Start of MOTD.</summary>
    public static IrcMessage MotdStart(string server, string nick, string serverName) =>
        CreateNumeric(server, NumericReply.RplMotdStart, nick,
            $"- {serverName} Message of the Day -");

    /// <summary>Creates RPL_MOTD (372) - Single MOTD line.</summary>
    public static IrcMessage Motd(string server, string nick, string line) =>
        CreateNumeric(server, NumericReply.RplMotd, nick, $"- {line}");

    /// <summary>Creates RPL_ENDOFMOTD (376) - End of MOTD.</summary>
    public static IrcMessage EndOfMotd(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplEndOfMotd, nick, "End of /MOTD command.");

    /// <summary>Creates ERR_NOMOTD (422) - No MOTD file available.</summary>
    public static IrcMessage NoMotd(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoMotd, nick, "MOTD File is missing");

    #endregion

    #region User/Channel Mode and Topic Numerics

    /// <summary>Creates RPL_UMODEIS (221) - User's current modes.</summary>
    public static IrcMessage UModeIs(string server, string nick, string modes) =>
        CreateNumeric(server, NumericReply.RplUModeIs, nick, modes);

    /// <summary>Creates RPL_CHANNELMODEIS (324) - Channel's current modes.</summary>
    public static IrcMessage ChannelModeIs(string server, string nick, string channel, string modes) =>
        CreateNumeric(server, NumericReply.RplChannelModeIs, nick, channel, modes);

    /// <summary>Creates RPL_CREATIONTIME (329) - Channel creation timestamp.</summary>
    public static IrcMessage CreationTime(string server, string nick, string channel, long timestamp) =>
        CreateNumeric(server, NumericReply.RplCreationTime, nick, channel, timestamp.ToString(Inv));

    /// <summary>Creates RPL_TOPIC (332) - Channel topic.</summary>
    public static IrcMessage Topic(string server, string nick, string channel, string topic) =>
        CreateNumeric(server, NumericReply.RplTopic, nick, channel, topic);

    /// <summary>Creates RPL_TOPICWHOTIME (333) - Topic setter and timestamp.</summary>
    public static IrcMessage TopicWhoTime(string server, string nick, string channel, string setBy, long timestamp) =>
        CreateNumeric(server, NumericReply.RplTopicWhoTime, nick, channel, setBy, timestamp.ToString(Inv));

    /// <summary>Creates RPL_NOTOPIC (331) - No topic is set.</summary>
    public static IrcMessage NoTopic(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.RplNoTopic, nick, channel, "No topic is set");

    /// <summary>Creates RPL_INVITELIST (346) - Channel invite mask entry.</summary>
    public static IrcMessage InviteList(string server, string nick, string channel, string mask) =>
        CreateNumeric(server, NumericReply.RplInviteList, nick, channel, mask);

    /// <summary>Creates RPL_ENDOFINVITELIST (347) - End of invite list.</summary>
    public static IrcMessage EndOfInviteList(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.RplEndOfInviteList, nick, channel, "End of channel invite list");

    /// <summary>Creates RPL_EXCEPTLIST (348) - Channel exception mask entry.</summary>
    public static IrcMessage ExceptList(string server, string nick, string channel, string mask) =>
        CreateNumeric(server, NumericReply.RplExceptList, nick, channel, mask);

    /// <summary>Creates RPL_ENDOFEXCEPTLIST (349) - End of exception list.</summary>
    public static IrcMessage EndOfExceptList(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.RplEndOfExceptList, nick, channel, "End of channel exception list");

    #endregion

    #region NAMES Numerics (353, 366)

    /// <summary>Creates RPL_NAMREPLY (353) - List of nicknames in a channel.</summary>
    public static IrcMessage NamReply(string server, string nick, string channelType, string channel, string names) =>
        CreateNumeric(server, NumericReply.RplNamReply, nick, channelType, channel, names);

    /// <summary>Creates RPL_ENDOFNAMES (366) - End of NAMES list.</summary>
    public static IrcMessage EndOfNames(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.RplEndOfNames, nick, channel, "End of /NAMES list");

    #endregion

    #region WHO/WHOIS/WHOWAS Numerics

    /// <summary>Creates RPL_WHOREPLY (352) - WHO response line.</summary>
    public static IrcMessage WhoReply(string server, string nick, string channel, string user, string host,
        string serverName, string targetNick, string status, int hopcount, string realname) =>
        CreateNumeric(server, NumericReply.RplWhoReply, nick,
            channel, user, host, serverName, targetNick, status, $"{hopcount} {realname}");

    /// <summary>Creates RPL_WHOSPCRPL (354) - WHOX extended response.</summary>
    /// <param name="server">Server name.</param>
    /// <param name="nick">Requesting user's nickname.</param>
    /// <param name="fields">The fields to include, in order based on the request flags.</param>
    public static IrcMessage WhoxReply(string server, string nick, params string[] fields) =>
        CreateNumeric(server, NumericReply.RplWhospcrpl, nick, fields);

    /// <summary>Creates RPL_ENDOFWHO (315) - End of WHO list.</summary>
    public static IrcMessage EndOfWho(string server, string nick, string mask) =>
        CreateNumeric(server, NumericReply.RplEndOfWho, nick, mask, "End of /WHO list");

    /// <summary>Creates RPL_WHOISUSER (311) - WHOIS user information.</summary>
    public static IrcMessage WhoisUser(string server, string nick, string targetNick, string user, string host, string realname) =>
        CreateNumeric(server, NumericReply.RplWhoisUser, nick, targetNick, user, host, "*", realname);

    /// <summary>Creates RPL_WHOISSERVER (312) - WHOIS server information.</summary>
    public static IrcMessage WhoisServer(string server, string nick, string targetNick, string serverName, string serverInfo) =>
        CreateNumeric(server, NumericReply.RplWhoisServer, nick, targetNick, serverName, serverInfo);

    /// <summary>Creates RPL_WHOISOPERATOR (313) - User is an IRC operator.</summary>
    public static IrcMessage WhoisOperator(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.RplWhoisOperator, nick, targetNick, "is an IRC operator");

    /// <summary>Creates RPL_WHOISIDLE (317) - User idle and signon time.</summary>
    public static IrcMessage WhoisIdle(string server, string nick, string targetNick, long idle, long signon) =>
        CreateNumeric(server, NumericReply.RplWhoisIdle, nick, targetNick, idle.ToString(Inv), signon.ToString(Inv), "seconds idle, signon time");

    /// <summary>Creates RPL_WHOISCHANNELS (319) - Channels the user is on.</summary>
    public static IrcMessage WhoisChannels(string server, string nick, string targetNick, string channels) =>
        CreateNumeric(server, NumericReply.RplWhoisChannels, nick, targetNick, channels);

    /// <summary>Creates RPL_WHOISACCOUNT (330) - User's account name.</summary>
    public static IrcMessage WhoisAccount(string server, string nick, string targetNick, string account) =>
        CreateNumeric(server, NumericReply.RplWhoisAccount, nick, targetNick, account, "is logged in as");

    /// <summary>Creates RPL_WHOISSECURE (671) - User is using TLS.</summary>
    public static IrcMessage WhoisSecure(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.RplWhoisSecure, nick, targetNick, "is using a secure connection");

    /// <summary>Creates RPL_WHOISHOST (378) - User's real host information.</summary>
    public static IrcMessage WhoisHost(string server, string nick, string targetNick, string info) =>
        CreateNumeric(server, NumericReply.RplWhoisHost, nick, targetNick, info);

    /// <summary>Creates RPL_ENDOFWHOIS (318) - End of WHOIS.</summary>
    public static IrcMessage EndOfWhois(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.RplEndOfWhois, nick, targetNick, "End of /WHOIS list");

    /// <summary>Creates RPL_WHOWASUSER (314) - WHOWAS user information.</summary>
    public static IrcMessage WhowasUser(string server, string nick, string targetNick, string user, string host, string realname) =>
        CreateNumeric(server, NumericReply.RplWhoWasUser, nick, targetNick, user, host, "*", realname);

    /// <summary>Creates RPL_ENDOFWHOWAS (369) - End of WHOWAS.</summary>
    public static IrcMessage EndOfWhowas(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.RplEndOfWhoWas, nick, targetNick, "End of WHOWAS");

    #endregion

    #region LIST Numerics (322, 323)

    /// <summary>Creates RPL_LIST (322) - Channel list entry.</summary>
    public static IrcMessage List(string server, string nick, string channel, int visible, string topic) =>
        CreateNumeric(server, NumericReply.RplList, nick, channel, visible.ToString(Inv), topic);

    /// <summary>Creates RPL_LISTEND (323) - End of LIST.</summary>
    public static IrcMessage ListEnd(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplListEnd, nick, "End of /LIST");

    #endregion

    #region BAN Numerics (367, 368)

    /// <summary>Creates RPL_BANLIST (367) - Channel ban entry.</summary>
    public static IrcMessage BanList(string server, string nick, string channel, string mask, string setBy, long setAt) =>
        CreateNumeric(server, NumericReply.RplBanList, nick, channel, mask, setBy, setAt.ToString(Inv));

    /// <summary>Creates RPL_ENDOFBANLIST (368) - End of ban list.</summary>
    public static IrcMessage EndOfBanList(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.RplEndOfBanList, nick, channel, "End of channel ban list");

    #endregion

    #region VERSION/TIME/ADMIN Numerics (256-259, 351, 391)

    /// <summary>Creates RPL_VERSION (351) - Server version information.</summary>
    public static IrcMessage Version(string server, string nick, string version, string debugLevel, string serverName, string comments) =>
        CreateNumeric(server, NumericReply.RplVersion, nick, $"{version}.{debugLevel}", serverName, comments);

    /// <summary>Creates RPL_TIME (391) - Server local time.</summary>
    public static IrcMessage Time(string server, string nick, string serverName, string timeString) =>
        CreateNumeric(server, NumericReply.RplTime, nick, serverName, timeString);

    /// <summary>Creates RPL_ADMINME (256) - Admin info server header.</summary>
    public static IrcMessage AdminMe(string server, string nick, string serverName) =>
        CreateNumeric(server, NumericReply.RplAdminMe, nick, serverName, "Administrative info");

    /// <summary>Creates RPL_ADMINLOC1 (257) - Admin location 1.</summary>
    public static IrcMessage AdminLoc1(string server, string nick, string info) =>
        CreateNumeric(server, NumericReply.RplAdminLoc1, nick, info);

    /// <summary>Creates RPL_ADMINLOC2 (258) - Admin location 2.</summary>
    public static IrcMessage AdminLoc2(string server, string nick, string info) =>
        CreateNumeric(server, NumericReply.RplAdminLoc2, nick, info);

    /// <summary>Creates RPL_ADMINEMAIL (259) - Admin email.</summary>
    public static IrcMessage AdminEmail(string server, string nick, string email) =>
        CreateNumeric(server, NumericReply.RplAdminEmail, nick, email);

    #endregion

    #region USERHOST/ISON Numerics (302, 303)

    /// <summary>Creates RPL_USERHOST (302) - USERHOST reply.</summary>
    public static IrcMessage UserHost(string server, string nick, string reply) =>
        CreateNumeric(server, NumericReply.RplUserHost, nick, reply);

    /// <summary>Creates RPL_ISON (303) - ISON reply.</summary>
    public static IrcMessage Ison(string server, string nick, string nicks) =>
        CreateNumeric(server, NumericReply.RplIsOn, nick, nicks);

    #endregion

    #region OPER Numerics (381, 382, 464)

    /// <summary>Creates ERR_PASSWDMISMATCH (464) - Password incorrect.</summary>
    public static IrcMessage PasswordMismatch(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrPasswdMismatch, nick, "Password incorrect");

    /// <summary>Creates RPL_REHASHING (382) - Server is rehashing config.</summary>
    public static IrcMessage Rehashing(string server, string nick, string configFile) =>
        CreateNumeric(server, NumericReply.RplRehashing, nick, configFile, "Rehashing");

    #endregion

    #region STATS Numerics (211, 212, 219, 242, 243)

    /// <summary>Creates RPL_STATSLINKINFO (211) - Stats link info.</summary>
    public static IrcMessage StatsLinkInfo(string server, string nick, string linkName, int sendQ, int sentMsgs, long sentKb, int recvMsgs, long recvKb) =>
        CreateNumeric(server, NumericReply.RplStatsLinkInfo, nick, linkName, sendQ.ToString(Inv), sentMsgs.ToString(Inv), sentKb.ToString(Inv), recvMsgs.ToString(Inv), recvKb.ToString(Inv));

    /// <summary>Creates RPL_STATSCOMMANDS (212) - Stats command usage.</summary>
    public static IrcMessage StatsCommands(string server, string nick, string command, int count, int byteCount, int remoteCount) =>
        CreateNumeric(server, NumericReply.RplStatsCommands, nick, command, count.ToString(Inv), byteCount.ToString(Inv), remoteCount.ToString(Inv));

    /// <summary>Creates RPL_ENDOFSTATS (219) - End of STATS.</summary>
    public static IrcMessage EndOfStats(string server, string nick, string query) =>
        CreateNumeric(server, NumericReply.RplEndOfStats, nick, query, "End of /STATS report");

    /// <summary>Creates RPL_STATSUPTIME (242) - Server uptime.</summary>
    public static IrcMessage StatsUptime(string server, string nick, int days, int hours, int minutes, int seconds) =>
        CreateNumeric(server, NumericReply.RplStatsUptime, nick, $"Server Up {days} days {hours}:{minutes:D2}:{seconds:D2}");

    /// <summary>Creates RPL_STATSOLINE (243) - O-line (operator) entry.</summary>
    public static IrcMessage StatsOline(string server, string nick, string hostmask, string name, string operClass) =>
        CreateNumeric(server, NumericReply.RplStatsOline, nick, "O", hostmask, "*", name, operClass);

    #endregion

    #region INFO Numerics (371, 374)

    /// <summary>Creates RPL_INFO (371) - Server info line.</summary>
    public static IrcMessage Info(string server, string nick, string text) =>
        CreateNumeric(server, NumericReply.RplInfo, nick, text);

    /// <summary>Creates RPL_ENDOFINFO (374) - End of INFO.</summary>
    public static IrcMessage EndOfInfo(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplEndOfInfo, nick, "End of /INFO list");

    #endregion

    #region AWAY Numerics (301, 305, 306)

    /// <summary>Creates RPL_AWAY (301) - User is away.</summary>
    public static IrcMessage Away(string server, string nick, string targetNick, string message) =>
        CreateNumeric(server, NumericReply.RplAway, nick, targetNick, message);

    /// <summary>Creates RPL_UNAWAY (305) - No longer marked away.</summary>
    public static IrcMessage UnAway(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplUnAway, nick, "You are no longer marked as being away");

    /// <summary>Creates RPL_NOWAWAY (306) - Now marked as away.</summary>
    public static IrcMessage NowAway(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplNowAway, nick, "You have been marked as being away");

    #endregion

    #region INVITE Numerics (341)

    /// <summary>Creates RPL_INVITING (341) - User was invited.</summary>
    public static IrcMessage Inviting(string server, string nick, string targetNick, string channel) =>
        CreateNumeric(server, NumericReply.RplInviting, nick, targetNick, channel);

    #endregion

    #region OPER Numerics (381)

    /// <summary>Creates RPL_YOUREOPER (381) - User is now an operator.</summary>
    public static IrcMessage YoureOper(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplYoureOper, nick, "You are now an IRC operator");

    #endregion

    #region SASL Numerics (900-904)

    /// <summary>Creates RPL_LOGGEDIN (900) - Successful SASL login.</summary>
    public static IrcMessage LoggedIn(string server, string nick, string hostmask, string account) =>
        CreateNumeric(server, NumericReply.RplLoggedIn, nick, hostmask, account, $"You are now logged in as {account}");

    /// <summary>Creates RPL_LOGGEDOUT (901) - Logged out from account.</summary>
    public static IrcMessage LoggedOut(string server, string nick, string hostmask) =>
        CreateNumeric(server, NumericReply.RplLoggedOut, nick, hostmask, "You are now logged out");

    /// <summary>Creates RPL_SASLSUCCESS (903) - SASL authentication succeeded.</summary>
    public static IrcMessage SaslSuccess(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplSaslSuccess, nick, "SASL authentication successful");

    /// <summary>Creates RPL_SASLMECHS (908) - Available SASL mechanisms.</summary>
    public static IrcMessage SaslMechanisms(string server, string nick, string mechanisms) =>
        CreateNumeric(server, NumericReply.RplSaslMechs, nick, mechanisms, "are available SASL mechanisms");

    #endregion

    #region Error Numerics (400-499)

    /// <summary>Creates ERR_NOSUCHNICK (401) - No such nickname.</summary>
    public static IrcMessage NoSuchNick(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.ErrNoSuchNick, nick, targetNick, "No such nick/channel");

    /// <summary>Creates ERR_NOSUCHSERVER (402) - No such server.</summary>
    public static IrcMessage NoSuchServer(string server, string nick, string serverName) =>
        CreateNumeric(server, NumericReply.ErrNoSuchServer, nick, serverName, "No such server");

    /// <summary>Creates ERR_NOSUCHCHANNEL (403) - No such channel.</summary>
    public static IrcMessage NoSuchChannel(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrNoSuchChannel, nick, channel, "No such channel");

    /// <summary>Creates ERR_CANNOTSENDTOCHAN (404) - Cannot send to channel.</summary>
    public static IrcMessage CannotSendToChannel(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrCannotSendToChan, nick, channel, "Cannot send to channel");

    /// <summary>Creates ERR_TOOMANYCHANNELS (405) - Joined too many channels.</summary>
    public static IrcMessage TooManyChannels(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrTooManyChannels, nick, channel, "You have joined too many channels");

    /// <summary>Creates ERR_WASNOSUCHNICK (406) - No such nickname in history.</summary>
    public static IrcMessage WasNoSuchNick(string server, string nick, string targetNick) =>
        CreateNumeric(server, NumericReply.ErrWasNoSuchNick, nick, targetNick, "There was no such nickname");

    /// <summary>Creates ERR_TOOMANYTARGETS (407) - Too many targets specified.</summary>
    public static IrcMessage TooManyTargets(string server, string nick, string target) =>
        CreateNumeric(server, NumericReply.ErrTooManyTargets, nick, target, "Too many targets");

    /// <summary>Creates ERR_NOORIGIN (409) - No origin specified for PING/PONG.</summary>
    public static IrcMessage NoOrigin(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoOrigin, nick, "No origin specified");

    /// <summary>Creates ERR_NORECIPIENT (411) - No recipient given.</summary>
    public static IrcMessage NoRecipient(string server, string nick, string command) =>
        CreateNumeric(server, NumericReply.ErrNoRecipient, nick, $"No recipient given ({command})");

    /// <summary>Creates ERR_NOTEXTTOSEND (412) - No text to send.</summary>
    public static IrcMessage NoTextToSend(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoTextToSend, nick, "No text to send");

    /// <summary>Creates ERR_UNKNOWNCOMMAND (421) - Unknown command.</summary>
    public static IrcMessage UnknownCommand(string server, string nick, string command) =>
        CreateNumeric(server, NumericReply.ErrUnknownCommand, nick, command, "Unknown command");

    /// <summary>Creates ERR_NONICKNAMEGIVEN (431) - No nickname given.</summary>
    public static IrcMessage NoNicknameGiven(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoNicknameGiven, nick, "No nickname given");

    /// <summary>Creates ERR_ERRONEUSNICKNAME (432) - Invalid nickname.</summary>
    public static IrcMessage ErroneusNickname(string server, string nick, string badNick) =>
        CreateNumeric(server, NumericReply.ErrErroneusNickname, nick, badNick, "Erroneous nickname");

    /// <summary>Creates ERR_NICKNAMEINUSE (433) - Nickname is in use.</summary>
    public static IrcMessage NicknameInUse(string server, string nick, string badNick) =>
        CreateNumeric(server, NumericReply.ErrNicknameInUse, nick, badNick, "Nickname is already in use");

    /// <summary>Creates ERR_USERNOTINCHANNEL (441) - User not in channel.</summary>
    public static IrcMessage UserNotInChannel(string server, string nick, string targetNick, string channel) =>
        CreateNumeric(server, NumericReply.ErrUserNotInChannel, nick, targetNick, channel, "They aren't on that channel");

    /// <summary>Creates ERR_NOTONCHANNEL (442) - You're not on that channel.</summary>
    public static IrcMessage NotOnChannel(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrNotOnChannel, nick, channel, "You're not on that channel");

    /// <summary>Creates ERR_USERONCHANNEL (443) - User is already on channel.</summary>
    public static IrcMessage UserOnChannel(string server, string nick, string targetNick, string channel) =>
        CreateNumeric(server, NumericReply.ErrUserOnChannel, nick, targetNick, channel, "is already on channel");

    /// <summary>Creates ERR_NOTREGISTERED (451) - User has not registered.</summary>
    public static IrcMessage NotRegistered(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNotRegistered, nick, "You have not registered");

    /// <summary>Creates ERR_NEEDMOREPARAMS (461) - Not enough parameters.</summary>
    public static IrcMessage NeedMoreParams(string server, string nick, string command) =>
        CreateNumeric(server, NumericReply.ErrNeedMoreParams, nick, command, "Not enough parameters");

    /// <summary>Creates ERR_ALREADYREGISTERED (462) - Already registered.</summary>
    public static IrcMessage AlreadyRegistered(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrAlreadyRegistered, nick, "You may not reregister");

    /// <summary>Creates ERR_PASSWDMISMATCH (464) - Password incorrect.</summary>
    public static IrcMessage PasswdMismatch(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrPasswdMismatch, nick, "Password incorrect");

    /// <summary>Creates ERR_YOUREBANNEDCREEP (465) - Banned from server.</summary>
    public static IrcMessage YoureBannedCreep(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrYoureBannedCreep, nick, "You are banned from this server");

    /// <summary>Creates ERR_CHANNELISFULL (471) - Channel is full (+l).</summary>
    public static IrcMessage ChannelIsFull(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrChannelIsFull, nick, channel, "Cannot join channel (+l)");

    /// <summary>Creates ERR_UNKNOWNMODE (472) - Unknown mode character.</summary>
    public static IrcMessage UnknownMode(string server, string nick, char mode) =>
        CreateNumeric(server, NumericReply.ErrUnknownMode, nick, mode.ToString(), "is unknown mode char to me");

    /// <summary>Creates ERR_INVITEONLYCHAN (473) - Channel is invite only (+i).</summary>
    public static IrcMessage InviteOnlyChan(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrInviteOnlyChan, nick, channel, "Cannot join channel (+i)");

    /// <summary>Creates ERR_BANNEDFROMCHAN (474) - Banned from channel (+b).</summary>
    public static IrcMessage BannedFromChan(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrBannedFromChan, nick, channel, "Cannot join channel (+b)");

    /// <summary>Creates ERR_BADCHANNELKEY (475) - Wrong channel key (+k).</summary>
    public static IrcMessage BadChannelKey(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrBadChannelKey, nick, channel, "Cannot join channel (+k)");

    /// <summary>Creates ERR_BADCHANMASK (476) - Bad channel mask.</summary>
    public static IrcMessage BadChannelMask(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrBadChanMask, nick, channel, "Bad Channel Mask");

    /// <summary>Creates ERR_NEEDREGGEDNICK (477) - Channel requires registered nickname (+R).</summary>
    public static IrcMessage NeedReggedNick(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrNoChanModes, nick, channel, "Cannot join channel (+R) - you need to be identified with services");

    /// <summary>Creates ERR_NOPRIVILEGES (481) - Not an IRC operator.</summary>
    public static IrcMessage NoPrivileges(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoPrivileges, nick, "Permission Denied- You're not an IRC operator");

    /// <summary>Creates ERR_CHANOPRIVSNEEDED (482) - Not a channel operator.</summary>
    public static IrcMessage ChanOpPrivsNeeded(string server, string nick, string channel) =>
        CreateNumeric(server, NumericReply.ErrChanOpPrivsNeeded, nick, channel, "You're not channel operator");

    /// <summary>Creates ERR_CANTKILLSERVER (483) - Cannot kill a server.</summary>
    public static IrcMessage CantKillServer(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrCantKillServer, nick, "You can't kill a server!");

    /// <summary>Creates ERR_NOOPERHOST (491) - No O-lines for host.</summary>
    public static IrcMessage NoOperHost(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNoOperHost, nick, "No O-lines for your host");

    /// <summary>Creates ERR_UMODEUNKNOWNFLAG (501) - Unknown user mode flag.</summary>
    public static IrcMessage UModeUnknownFlag(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrUModeUnknownFlag, nick, "Unknown MODE flag");

    /// <summary>Creates ERR_USERSDONTMATCH (502) - Cannot change other users' modes.</summary>
    public static IrcMessage UsersDontMatch(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrUsersDontMatch, nick, "Can't change mode for other users");

    #endregion

    #region SASL Error Numerics (902-907)

    /// <summary>Creates ERR_NICKLOCKED (902) - Must use assigned nick.</summary>
    public static IrcMessage NickLocked(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrNickLocked, nick, "You must use a nick assigned to you");

    /// <summary>Creates ERR_SASLFAIL (904) - SASL authentication failed.</summary>
    public static IrcMessage SaslFail(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrSaslFail, nick, "SASL authentication failed");

    /// <summary>Creates ERR_SASLTOOLONG (905) - SASL message too long.</summary>
    public static IrcMessage SaslTooLong(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrSaslTooLong, nick, "SASL message too long");

    /// <summary>Creates ERR_SASLABORTED (906) - SASL authentication aborted.</summary>
    public static IrcMessage SaslAborted(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrSaslAborted, nick, "SASL authentication aborted");

    /// <summary>Creates ERR_SASLALREADY (907) - Already authenticated via SASL.</summary>
    public static IrcMessage SaslAlready(string server, string nick) =>
        CreateNumeric(server, NumericReply.ErrSaslAlready, nick, "You have already authenticated using SASL");

    #endregion

    #region CAP Error Numerics

    /// <summary>Creates error for invalid CAP command.</summary>
    /// <remarks>Uses ERR_UNKNOWNERROR as ERR_INVALIDCAPCMD (410) is not in our enum.</remarks>
    public static IrcMessage InvalidCapCmd(string server, string nick, string command) =>
        CreateNumeric(server, NumericReply.ErrUnknownError, nick, command, "Invalid CAP command");

    #endregion

    #region IRCv3 Standard Replies

    /// <summary>
    /// Creates an IRCv3 standard reply (FAIL, WARN, NOTE).
    /// Format: :server FAIL/WARN/NOTE COMMAND CODE context :human-readable
    /// </summary>
    /// <param name="server">The server name.</param>
    /// <param name="type">The reply type (FAIL, WARN, or NOTE).</param>
    /// <param name="command">The command this reply is for.</param>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="context">Additional context (e.g., nick, target).</param>
    /// <param name="message">Human-readable message.</param>
    public static IrcMessage StandardReply(string server, string type, string command, string code, string context, string message)
    {
        return IrcMessage.CreateWithSource(server, type, command, code, context, message);
    }

    /// <summary>
    /// Creates an IRCv3 FAIL standard reply.
    /// </summary>
    public static IrcMessage Fail(string server, string command, string code, string context, string message) =>
        StandardReply(server, "FAIL", command, code, context, message);

    /// <summary>
    /// Creates an IRCv3 WARN standard reply.
    /// </summary>
    public static IrcMessage Warn(string server, string command, string code, string context, string message) =>
        StandardReply(server, "WARN", command, code, context, message);

    /// <summary>
    /// Creates an IRCv3 NOTE standard reply.
    /// </summary>
    public static IrcMessage Note(string server, string command, string code, string context, string message) =>
        StandardReply(server, "NOTE", command, code, context, message);

    #endregion

    #region Links Numerics (364-365)

    /// <summary>Creates RPL_LINKS (364) - Server link information.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="mask">The mask being searched for.</param>
    /// <param name="linkedServer">The linked server name.</param>
    /// <param name="hopCount">Number of hops to the server.</param>
    /// <param name="serverInfo">Server description.</param>
    public static IrcMessage Links(string server, string nick, string mask, string linkedServer, int hopCount, string serverInfo) =>
        CreateNumeric(server, NumericReply.RplLinks, nick, mask, linkedServer, $"{hopCount} {serverInfo}");

    /// <summary>Creates RPL_ENDOFLINKS (365) - End of LINKS list.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="mask">The mask that was searched.</param>
    public static IrcMessage EndOfLinks(string server, string nick, string mask) =>
        CreateNumeric(server, NumericReply.RplEndOfLinks, nick, mask, "End of LINKS list");

    #endregion

    #region Trace Numerics (200-209, 261-262)

    /// <summary>Creates RPL_TRACELINK (200) - Link being followed for trace.</summary>
    public static IrcMessage TraceLink(string server, string nick, string version, string nextServer, string debugLevel) =>
        CreateNumeric(server, NumericReply.RplTraceLink, nick, "Link", version, nextServer, debugLevel);

    /// <summary>Creates RPL_TRACECONNECTING (201) - Connecting server.</summary>
    public static IrcMessage TraceConnecting(string server, string nick, string className, string serverName) =>
        CreateNumeric(server, NumericReply.RplTraceConnecting, nick, "Try.", className, serverName);

    /// <summary>Creates RPL_TRACEHANDSHAKE (202) - Server in handshake phase.</summary>
    public static IrcMessage TraceHandshake(string server, string nick, string className, string serverName) =>
        CreateNumeric(server, NumericReply.RplTraceHandshake, nick, "H.S.", className, serverName);

    /// <summary>Creates RPL_TRACEUNKNOWN (203) - Unknown connection.</summary>
    public static IrcMessage TraceUnknown(string server, string nick, string className, string clientAddress) =>
        CreateNumeric(server, NumericReply.RplTraceUnknown, nick, "????", className, clientAddress);

    /// <summary>Creates RPL_TRACEOPERATOR (204) - IRC operator.</summary>
    public static IrcMessage TraceOperator(string server, string nick, string className, string operNick) =>
        CreateNumeric(server, NumericReply.RplTraceOperator, nick, "Oper", className, operNick);

    /// <summary>Creates RPL_TRACEUSER (205) - User connection.</summary>
    public static IrcMessage TraceUser(string server, string nick, string className, string userNick) =>
        CreateNumeric(server, NumericReply.RplTraceUser, nick, "User", className, userNick);

    /// <summary>Creates RPL_TRACESERVER (206) - Server connection.</summary>
    public static IrcMessage TraceServer(string server, string nick, string className, int serverCount, int clientCount, 
        string serverName, string uplink) =>
        CreateNumeric(server, NumericReply.RplTraceServer, nick, "Serv", className, 
            $"{serverCount}S", $"{clientCount}C", serverName, $"*!*@{uplink}");

    /// <summary>Creates RPL_TRACEEND (262) - End of TRACE output.</summary>
    public static IrcMessage TraceEnd(string server, string nick, string targetServer, string version) =>
        CreateNumeric(server, NumericReply.RplTraceEnd, nick, targetServer, version, "End of TRACE");

    #endregion

    #region MONITOR Numerics (730-734)

    /// <summary>Creates RPL_MONONLINE (730) - Target is online.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="targets">Comma-separated list of nick!user@host for online targets.</param>
    public static IrcMessage MonOnline(string server, string nick, string targets) =>
        CreateNumeric(server, NumericReply.RplMonOnline, nick, targets);

    /// <summary>Creates RPL_MONOFFLINE (731) - Target is offline.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="targets">Comma-separated list of nicknames that are offline.</param>
    public static IrcMessage MonOffline(string server, string nick, string targets) =>
        CreateNumeric(server, NumericReply.RplMonOffline, nick, targets);

    /// <summary>Creates RPL_MONLIST (732) - Entry in monitor list.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="targets">Comma-separated list of nicknames being monitored.</param>
    public static IrcMessage MonList(string server, string nick, string targets) =>
        CreateNumeric(server, NumericReply.RplMonList, nick, targets);

    /// <summary>Creates RPL_ENDOFMONLIST (733) - End of monitor list.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    public static IrcMessage EndOfMonList(string server, string nick) =>
        CreateNumeric(server, NumericReply.RplEndOfMonList, nick, "End of MONITOR list");

    /// <summary>Creates ERR_MONLISTFULL (734) - Monitor list is full.</summary>
    /// <param name="server">The server name.</param>
    /// <param name="nick">Target nickname.</param>
    /// <param name="limit">Maximum allowed monitor list size.</param>
    /// <param name="targets">The nicknames that couldn't be added.</param>
    public static IrcMessage MonListFull(string server, string nick, int limit, string targets) =>
        CreateNumeric(server, NumericReply.ErrMonListFull, nick, 
            limit.ToString(CultureInfo.InvariantCulture), targets, "Monitor list is full");

    #endregion
}
