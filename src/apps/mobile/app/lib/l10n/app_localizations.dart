import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_zh.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('zh'),
  ];

  /// No description provided for @appTitle.
  ///
  /// In en, this message translates to:
  /// **'Bakabase'**
  String get appTitle;

  /// No description provided for @connectTitle.
  ///
  /// In en, this message translates to:
  /// **'Connect to Bakabase'**
  String get connectTitle;

  /// No description provided for @connecting.
  ///
  /// In en, this message translates to:
  /// **'Connecting…'**
  String get connecting;

  /// No description provided for @onThisNetwork.
  ///
  /// In en, this message translates to:
  /// **'On this network'**
  String get onThisNetwork;

  /// No description provided for @discoveryHint.
  ///
  /// In en, this message translates to:
  /// **'Searching… Make sure Bakabase is running with remote access enabled, and that this device is on the same network. On iOS, allow local network access when asked.'**
  String get discoveryHint;

  /// No description provided for @remembered.
  ///
  /// In en, this message translates to:
  /// **'Remembered'**
  String get remembered;

  /// No description provided for @rememberedEmpty.
  ///
  /// In en, this message translates to:
  /// **'Servers you connect to are remembered here.'**
  String get rememberedEmpty;

  /// No description provided for @byAddress.
  ///
  /// In en, this message translates to:
  /// **'By address'**
  String get byAddress;

  /// No description provided for @connect.
  ///
  /// In en, this message translates to:
  /// **'Connect'**
  String get connect;

  /// No description provided for @couldNotConnect.
  ///
  /// In en, this message translates to:
  /// **'Could not connect to {url}'**
  String couldNotConnect(String url);

  /// No description provided for @remoteAccessDisabledHint.
  ///
  /// In en, this message translates to:
  /// **'Remote access is turned off. Enable it in Bakabase on the host machine (Settings → Remote access).'**
  String get remoteAccessDisabledHint;

  /// No description provided for @protocolTooNew.
  ///
  /// In en, this message translates to:
  /// **'Server speaks protocol v{version}; this app is too old for it. Update the app.'**
  String protocolTooNew(String version);

  /// No description provided for @protocolTooOld.
  ///
  /// In en, this message translates to:
  /// **'Server speaks protocol v{version}; this app needs a newer server. Update Bakabase on the host.'**
  String protocolTooOld(String version);

  /// No description provided for @searchHint.
  ///
  /// In en, this message translates to:
  /// **'Search resources'**
  String get searchHint;

  /// No description provided for @allLibraries.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get allLibraries;

  /// No description provided for @sortTooltip.
  ///
  /// In en, this message translates to:
  /// **'Sort'**
  String get sortTooltip;

  /// No description provided for @sortAddDt.
  ///
  /// In en, this message translates to:
  /// **'Recently added'**
  String get sortAddDt;

  /// No description provided for @sortPlayedAt.
  ///
  /// In en, this message translates to:
  /// **'Recently played'**
  String get sortPlayedAt;

  /// No description provided for @sortFileModifyDt.
  ///
  /// In en, this message translates to:
  /// **'File modified'**
  String get sortFileModifyDt;

  /// No description provided for @sortFilename.
  ///
  /// In en, this message translates to:
  /// **'Filename'**
  String get sortFilename;

  /// No description provided for @ascending.
  ///
  /// In en, this message translates to:
  /// **'Ascending ↑'**
  String get ascending;

  /// No description provided for @descending.
  ///
  /// In en, this message translates to:
  /// **'Descending ↓'**
  String get descending;

  /// No description provided for @playHistoryTooltip.
  ///
  /// In en, this message translates to:
  /// **'Play history'**
  String get playHistoryTooltip;

  /// No description provided for @switchServerTooltip.
  ///
  /// In en, this message translates to:
  /// **'Switch server'**
  String get switchServerTooltip;

  /// No description provided for @openWebUiTooltip.
  ///
  /// In en, this message translates to:
  /// **'Open full web UI'**
  String get openWebUiTooltip;

  /// No description provided for @playHere.
  ///
  /// In en, this message translates to:
  /// **'Play here'**
  String get playHere;

  /// No description provided for @builtInPlayer.
  ///
  /// In en, this message translates to:
  /// **'Built-in player (mpv)'**
  String get builtInPlayer;

  /// No description provided for @copyStreamLink.
  ///
  /// In en, this message translates to:
  /// **'Copy stream link'**
  String get copyStreamLink;

  /// No description provided for @streamLinkCopied.
  ///
  /// In en, this message translates to:
  /// **'Stream link copied — paste it into a player'**
  String get streamLinkCopied;

  /// No description provided for @couldNotOpenPlayer.
  ///
  /// In en, this message translates to:
  /// **'Could not open {name} — is it installed?'**
  String couldNotOpenPlayer(String name);

  /// No description provided for @otherPlayer.
  ///
  /// In en, this message translates to:
  /// **'Other player…'**
  String get otherPlayer;

  /// No description provided for @readPages.
  ///
  /// In en, this message translates to:
  /// **'Read ({count} pages)'**
  String readPages(int count);

  /// No description provided for @playSection.
  ///
  /// In en, this message translates to:
  /// **'Play'**
  String get playSection;

  /// No description provided for @otherFiles.
  ///
  /// In en, this message translates to:
  /// **'Other files'**
  String get otherFiles;

  /// No description provided for @noPlayableFiles.
  ///
  /// In en, this message translates to:
  /// **'No playable files were found in this resource.'**
  String get noPlayableFiles;

  /// No description provided for @playHistory.
  ///
  /// In en, this message translates to:
  /// **'Play history'**
  String get playHistory;

  /// No description provided for @removedResource.
  ///
  /// In en, this message translates to:
  /// **'Resource #{id} (removed)'**
  String removedResource(int id);
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'zh'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'zh':
      return AppLocalizationsZh();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
