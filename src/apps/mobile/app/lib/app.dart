import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/connection.dart';
import 'core/analytics.dart';
import 'features/connect/connect_page.dart';
import 'features/connect/pair_page.dart';
import 'features/library/library_page.dart';
import 'l10n/app_localizations.dart';

class BakabaseApp extends ConsumerStatefulWidget {
  const BakabaseApp({super.key});

  @override
  ConsumerState<BakabaseApp> createState() => _BakabaseAppState();
}

class _BakabaseAppState extends ConsumerState<BakabaseApp>
    with WidgetsBindingObserver {
  Timer? _analyticsRefresh;

  void _startAnalyticsRefresh() {
    _analyticsRefresh?.cancel();
    _analyticsRefresh = Timer.periodic(const Duration(minutes: 4), (_) {
      unawaited(ref.read(connectionProvider.notifier).refreshAnalytics());
    });
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(ref.read(mobileAnalyticsProvider).initialize());
    _startAnalyticsRefresh();

    // Once, at startup, rather than whenever the picker appears: reconnecting from
    // the picker would make "switch server" bounce straight back to the server the
    // user just left.
    ref.read(connectionProvider.notifier).resumeLastServer();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      _startAnalyticsRefresh();
      unawaited(
        ref
            .read(connectionProvider.notifier)
            .refreshAnalytics(newSession: true),
      );
    } else if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.detached) {
      _analyticsRefresh?.cancel();
      ref.read(connectionProvider.notifier).suspendAnalytics();
    }
  }

  @override
  void dispose() {
    _analyticsRefresh?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final connection = ref.watch(connectionProvider);
    ref.watch(mobileAnalyticsProvider);
    if (connection is Connected) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) connection.api.analytics?.capture('ui_ready');
      });
    }

    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context)!.appTitle,
      // English + Chinese, following the system locale by default (the
      // MaterialApp default resolution picks the closest supported locale).
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF0E7C6B)),
      ),
      darkTheme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF0E7C6B),
          brightness: Brightness.dark,
        ),
      ),
      // The app cannot exist without a server: everything except the connect
      // flow lives behind a successful handshake. Pairing sits in between — the
      // server is reachable and understood, it just will not serve a device it
      // does not know yet.
      home: switch (connection) {
        Connected() => const LibraryPage(),
        NeedsPairing target => PairPage(target: target),
        _ => const ConnectPage(),
      },
    );
  }
}
