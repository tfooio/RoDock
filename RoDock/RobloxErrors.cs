namespace RoDock;

/// What to do after a failure: retry with the normal backoff, wait longer first, or stop and tell the user.
public enum Act { Retry, Slow, Stop }

/// Roblox's own error numbers (Enum.ConnectionError, create.roblox.com/docs/reference/engine/enums/ConnectionError, docs of 2026-08-06)
/// and what RoDock does about each. R = retry normally. S = wait at least 2 minutes first (maintenance, full or flooded servers,
/// kicks, same account open elsewhere, client needs updating). X = stop and alert: retrying cannot help, or could hurt the account.
/// Numbers missing from this table are treated as R.
public static class RobloxErrors
{
    const string Table = """
        1 Unknown R
        2 ConnectErrors R
        3 AlreadyConnected R
        4 NoFreeIncomingConnections R
        5 ConnectionBanned X
        6 InvalidPassword R
        7 IncompatibleProtocolVersion S
        8 IPRecentlyConnected S
        9 OurSystemRequiresSecurity R
        10 SecurityKeyMismatch R
        256 DisconnectErrors R
        257 DisconnectBadhash R
        258 DisconnectSecurityKeyMismatch R
        259 DisconnectProtocolMismatch S
        260 DisconnectReceivePacketError R
        261 DisconnectReceivePacketStreamError R
        262 DisconnectSendPacketError R
        263 DisconnectIllegalTeleport R
        264 DisconnectDuplicatePlayer S
        265 DisconnectDuplicateTicket S
        266 DisconnectTimeout R
        267 DisconnectLuaKick S
        268 DisconnectOnRemoteSysStats R
        269 DisconnectHashTimeout R
        270 DisconnectCloudEditKick R
        271 DisconnectPlayerless R
        272 DisconnectNewSecurityKeyMismatch R
        273 DisconnectEvicted S
        274 DisconnectDevMaintenance S
        275 DisconnectRobloxMaintenance S
        276 DisconnectRejoin R
        277 DisconnectConnectionLost R
        278 DisconnectIdle R
        279 DisconnectRaknetErrors R
        280 DisconnectWrongVersion S
        281 DisconnectBySecurityPolicy X
        282 DisconnectBlockedIP X
        284 DisconnectClientFailure R
        285 DisconnectClientRequest R
        286 DisconnectPrivateServerKickout X
        287 DisconnectModeratedGame X
        288 ServerShutdown R
        290 ReplicatorTimeout R
        291 PlayerRemoved R
        292 DisconnectOutOfMemoryKeepPlayingLeave R
        293 DisconnectRomarkEndOfTest R
        294 DisconnectCollaboratorPermissionRevoked X
        295 DisconnectCollaboratorUnderage X
        296 NetworkInternal R
        297 NetworkSend R
        298 NetworkTimeout R
        299 NetworkMisbehavior S
        300 NetworkSecurity S
        301 ReplacementReady R
        302 ServerEmpty R
        303 PhantomFreeze R
        304 AndroidAnticheatKick X
        305 AndroidEmulatorKick X
        306 AndroidRootedKick X
        307 ScreentimeLockoutKick X
        308 DisconnectionNotification R
        309 DisconnectVerboselyModeratedGame X
        310 DisconnectCollaboratorNotAgeVerified X
        311 DisconnectCollaboratorTrustedConnectionsRequired X
        312 DisconnectCollaboratorOwnerActionRequired X
        313 DisconnectCollaboratorTooManyCollaborators X
        314 DisconnectCollaboratorUnknownError X
        512 PlacelaunchErrors R
        515 PlacelaunchDisabled X
        516 PlacelaunchError R
        517 PlacelaunchGameEnded R
        518 PlacelaunchGameFull S
        522 PlacelaunchUserLeft R
        523 PlacelaunchRestricted X
        524 PlacelaunchUnauthorized X
        525 PlacelaunchFlooded S
        526 PlacelaunchHashExpired R
        527 PlacelaunchHashException R
        528 PlacelaunchPartyCannotFit X
        529 PlacelaunchHttpError R
        533 PlacelaunchUserPrivacyUnauthorized X
        541 PlacelaunchVipOwnerNotPresent S
        542 PlacelaunchAgeVerificationRequired X
        543 PlacelaunchParentalApprovalRequired X
        544 PlacelaunchCoreGated X
        600 PlacelaunchCreatorBan X
        601 PlacelaunchDeviceBlock X
        610 PlacelaunchCustomMessage S
        611 PlacelaunchOtherError S
        768 TeleportErrors R
        769 TeleportFailure R
        770 TeleportGameNotFound X
        771 TeleportGameEnded R
        772 TeleportGameFull S
        773 TeleportUnauthorized X
        774 TeleportFlooded S
        775 TeleportIsTeleporting R
        """;

    static readonly Dictionary<int, (string Name, Act Act)> Codes = Table
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(l => l.Split(' '))
        .ToDictionary(p => int.Parse(p[0]), p => (p[1], p[2] switch { "S" => Act.Slow, "X" => Act.Stop, _ => Act.Retry }));

    public static int Count => Codes.Count;

    /// Message and action for a Roblox error number; null means the log showed a failure without a number.
    public static (string Text, Act Act) Describe(int? code) => code switch
    {
        { } c when Codes.TryGetValue(c, out var e) => ($"Error {c} ({e.Name})", e.Act),
        { } c => ($"Error {c} (unknown code)", Act.Retry),
        null => ("Roblox reported a connection error", Act.Retry),
    };
}
