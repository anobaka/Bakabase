import 'dart:async';

import 'package:bakabase_mobile/core/analytics.dart';
import 'package:bakabase_mobile/core/api_client.dart';
import 'package:bakabase_mobile/core/connection.dart';
import 'package:bakabase_mobile/core/models.dart';
import 'package:bakabase_mobile/features/library/library_page.dart';
import 'package:bakabase_mobile/l10n/app_localizations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'analytics_test.dart' show config;

class _Api extends BakabaseApiClient {
  _Api() : super('http://nas:34567');
  Completer<SearchResult>? pending;
  int searches = 0;

  @override
  Future<List<MediaLibrary>> mediaLibraries() async => const [
    MediaLibrary(id: 1, name: 'Test library', resourceCount: 0),
  ];

  @override
  Future<SearchResult> search({
    String? keyword,
    int? mediaLibraryId,
    int page = 1,
    int pageSize = 60,
    int? sortProperty,
    bool sortAsc = false,
  }) async {
    searches++;
    return pending?.future ??
        Future.value(
          SearchResult(
            resources: const [],
            totalCount: 0,
            page: page,
            pageSize: pageSize,
          ),
        );
  }
}

class _Connection extends ConnectionController {
  _Connection(this.connected);
  final Connected connected;
  @override
  ServerConnectionState build() => connected;
}

void main() {
  late MobileAnalytics analytics;
  late _Api api;
  late List<Map<String, dynamic>> events;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    events = [];
    analytics = MobileAnalytics(
      version: () async => '0.1.0+1',
      send: (_, payload, _) async {
        events.addAll((payload['batch'] as List).cast<Map<String, dynamic>>());
      },
    );
    await analytics.initialize();
    api = _Api()
      ..analytics = analytics.configure(config(), 'http://nas:34567', 1);
  });
  tearDown(() => analytics.dispose());

  Future<void> open(WidgetTester tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          connectionProvider.overrideWith(
            () => _Connection(
              Connected(
                api,
                const ServerInfo(
                  id: 'test-server',
                  name: 'Test server',
                  appVersion: '2.4.0',
                  protocolVersion: 1,
                ),
              ),
            ),
          ),
        ],
        child: MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: const LibraryPage(),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  List<Map> queries() => events
      .where((e) => e['event'] == 'resource_query_presented')
      .map((e) => e['properties'] as Map)
      .toList();

  testWidgets(
    'initial load, debounced user submission and refresh have distinct search kinds',
    (tester) async {
      await open(tester);
      await analytics.flush();
      expect(queries().single['search_kind'], 'restored');
      expect(queries().single['load_mode'], 'replace');
      expect(queries().single['feature_id'], 'search');

      await tester.enterText(find.byType(TextField), 'private search');
      await tester.pump(const Duration(milliseconds: 399));
      expect(api.searches, 1);
      await tester.pump(const Duration(milliseconds: 1));
      await tester.pumpAndSettle();
      await analytics.flush();
      expect(queries().last['search_kind'], 'manual');
      expect(queries().last['has_keyword'], isTrue);
      expect(queries().last['load_mode'], 'replace');
      expect(queries().last.values, isNot(contains('private search')));

    await tester.tap(find.text('Test library (0)'));
      await tester.pumpAndSettle();
      await analytics.flush();
      expect(queries().last['search_kind'], 'manual');
      expect(queries().last['has_filters'], isTrue);

      await tester
          .widget<RefreshIndicator>(find.byType(RefreshIndicator))
          .onRefresh();
      await tester.pumpAndSettle();
      await analytics.flush();
      expect(queries().last['search_kind'], 'refresh');
      expect(queries().last['load_mode'], 'replace');
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );

  testWidgets(
    'a late result cannot attach to renewed consent after its request began',
    (tester) async {
      api.pending = Completer<SearchResult>();
      await open(tester);
      analytics.clearContext();
      api.analytics = analytics.configure(config(), 'http://nas:34567', 1);
      api.pending!.complete(
        const SearchResult(resources: [], totalCount: 0, page: 1, pageSize: 60),
      );
      await tester.pumpAndSettle();
      await analytics.flush();
      expect(queries(), isEmpty);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
}
