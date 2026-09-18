// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'Bakabase';

  @override
  String get connectTitle => 'Connect to Bakabase';

  @override
  String get connecting => 'Connecting…';

  @override
  String get onThisNetwork => 'On this network';

  @override
  String get discoveryHint =>
      'Searching… Make sure Bakabase is running with remote access enabled, and that this device is on the same network. On iOS, allow local network access when asked.';

  @override
  String get remembered => 'Remembered';

  @override
  String get rememberedEmpty => 'Servers you connect to are remembered here.';

  @override
  String get byAddress => 'By address';

  @override
  String get connect => 'Connect';

  @override
  String couldNotConnect(String url) {
    return 'Could not connect to $url';
  }

  @override
  String get remoteAccessDisabledHint =>
      'Remote access is turned off. Enable it in Bakabase on the host machine (Settings → Remote access).';

  @override
  String protocolTooNew(String version) {
    return 'Server speaks protocol v$version; this app is too old for it. Update the app.';
  }

  @override
  String protocolTooOld(String version) {
    return 'Server speaks protocol v$version; this app needs a newer server. Update Bakabase on the host.';
  }

  @override
  String get searchHint => 'Search resources';

  @override
  String get allLibraries => 'All';

  @override
  String get sortTooltip => 'Sort';

  @override
  String get sortAddDt => 'Recently added';

  @override
  String get sortPlayedAt => 'Recently played';

  @override
  String get sortFileModifyDt => 'File modified';

  @override
  String get sortFilename => 'Filename';

  @override
  String get ascending => 'Ascending ↑';

  @override
  String get descending => 'Descending ↓';

  @override
  String get playHistoryTooltip => 'Play history';

  @override
  String get switchServerTooltip => 'Switch server';

  @override
  String get openWebUiTooltip => 'Open full web UI';

  @override
  String get playHere => 'Play here';

  @override
  String get builtInPlayer => 'Built-in player (mpv)';

  @override
  String get copyStreamLink => 'Copy stream link';

  @override
  String get streamLinkCopied => 'Stream link copied — paste it into a player';

  @override
  String couldNotOpenPlayer(String name) {
    return 'Could not open $name — is it installed?';
  }

  @override
  String get otherPlayer => 'Other player…';

  @override
  String readPages(int count) {
    return 'Read ($count pages)';
  }

  @override
  String get playSection => 'Play';

  @override
  String get otherFiles => 'Other files';

  @override
  String get noPlayableFiles =>
      'No playable files were found in this resource.';

  @override
  String get playHistory => 'Play history';

  @override
  String removedResource(int id) {
    return 'Resource #$id (removed)';
  }

  @override
  String get pairTitle => 'Pair this device';

  @override
  String get pairIntro =>
      'This server only serves devices it knows. Read the code from Bakabase on the server, or ask someone at a paired device to approve this one.';

  @override
  String get pairCodeLabel => '6-digit code';

  @override
  String get pairSubmit => 'Pair';

  @override
  String get pairAskInstead => 'Ask for approval instead';

  @override
  String get pairWaiting => 'Waiting for someone to approve this device…';

  @override
  String get pairCodeRejected =>
      'That code was wrong or has expired. Read a fresh one from the server.';

  @override
  String get pairRequestRejected =>
      'The request was turned down, or waited too long.';

  @override
  String get pairTooManyAttempts =>
      'The server is not taking more attempts from here for now. Try again in a few minutes.';

  @override
  String get pairUnreachable => 'The server stopped answering.';

  @override
  String get pairUnsupported => 'This server is too old to pair with.';

  @override
  String get pairDeviceNameLabel => 'Device name';

  @override
  String get denialUnauthenticated =>
      'This device is not paired with that server.';

  @override
  String get denialSignatureExpired =>
      'This phone\'s clock is off, so the server rejected the request. Fix the date and time, then try again.';

  @override
  String get denialDeviceRevoked =>
      'This device was unpaired from that server. Pair it again.';

  @override
  String get denialRunsOnUserMachine =>
      'That happens on the machine holding the files, so it cannot run from here.';

  @override
  String get cancel => 'Cancel';

  @override
  String get save => 'Save';

  @override
  String get retry => 'Try again';

  @override
  String get devicesTooltip => 'Devices';

  @override
  String get devicesTitle => 'Devices';

  @override
  String get devicesWaiting => 'Waiting to be let in';

  @override
  String get devicesPaired => 'Devices with access';

  @override
  String get devicesNone => 'No devices are paired with this server.';

  @override
  String get devicesUnknownAddress => 'Address unknown';

  @override
  String get devicesApprove => 'Approve';

  @override
  String get devicesReject => 'Reject';

  @override
  String get devicesThisDevice => 'This device';

  @override
  String get devicesNeverSeen => 'Has not connected yet';

  @override
  String get devicesSeenJustNow => 'Seen just now';

  @override
  String devicesSeenMinutesAgo(int minutes) {
    return 'Seen $minutes min ago';
  }

  @override
  String devicesSeenHoursAgo(int hours) {
    return 'Seen $hours h ago';
  }

  @override
  String devicesSeenDaysAgo(int days) {
    return 'Seen $days d ago';
  }

  @override
  String get deviceRenameTitle => 'Rename';

  @override
  String get deviceRevoke => 'Remove access';

  @override
  String deviceRevokeTitle(String name) {
    return 'Remove $name?';
  }

  @override
  String get deviceRevokeBody =>
      'That device will have to pair again before it can reach this server.';

  @override
  String get deviceRevokeSelfBody =>
      'This is the device you are using. Removing its access disconnects you from this server, and you will have to pair again to come back.';

  @override
  String get pairVoluntaryIntro =>
      'This server serves this device already. Pairing names it, lets you manage who else has access, and keeps working if the server is later closed to unknown devices.';

  @override
  String get pairCancel => 'Not now';

  @override
  String get notPairedTitle => 'This device is not paired';

  @override
  String get notPairedBody =>
      'Pair it to manage devices and keep access if this server stops serving unknown ones.';

  @override
  String get notPairedAction => 'Pair';

  @override
  String get forgetServer => 'Forget';

  @override
  String forgetServerTitle(String name) {
    return 'Forget $name?';
  }

  @override
  String get forgetServerBody =>
      'This removes the server from the list. If this device is paired with it, its key is deleted too and pairing again means a new code or approval.';

  @override
  String get forgetServerConfirm => 'Forget';
}
