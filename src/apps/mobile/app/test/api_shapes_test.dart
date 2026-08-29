import 'package:bakabase_mobile/core/api_client.dart';
import 'package:bakabase_mobile/core/models.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('search body DSL', () {
    test('library filter is Internal/MediaLibraryV2Multi/In with a ListString operand', () {
      final body = BakabaseApiClient.buildSearchBody(mediaLibraryId: 5, page: 2, pageSize: 30);

      expect(body['page'], 2);
      expect(body['pageSize'], 30);
      final filter =
          ((body['group'] as Map)['filters'] as List).single as Map<String, dynamic>;
      expect(filter['propertyPool'], 1);
      expect(filter['propertyId'], 25);
      expect(filter['operation'], 15);
      expect(filter['dbValue'], '5');
    });

    test('orders carry the sortable property and direction', () {
      final body = BakabaseApiClient.buildSearchBody(sortProperty: 11, sortAsc: true);

      final order = (body['orders'] as List).single as Map<String, dynamic>;
      expect(order['property'], 11);
      expect(order['asc'], true);
    });

    test('blank keyword and absent filters are omitted', () {
      final body = BakabaseApiClient.buildSearchBody(keyword: '  ');

      expect(body.containsKey('keyword'), isFalse);
      expect(body.containsKey('group'), isFalse);
      expect(body.containsKey('orders'), isFalse);
    });
  });

  group('rating extraction', () {
    test('reads properties[Reserved=2][Rating=13].values[0].value', () {
      final rating = BakabaseApiClient.ratingFromResourceJson({
        'id': 1,
        'properties': {
          '2': {
            '13': {
              'values': [
                {'scope': 0, 'value': 4.5},
              ],
            },
          },
        },
      });

      expect(rating, 4.5);
    });

    test('is null when the property, values, or value are absent', () {
      expect(BakabaseApiClient.ratingFromResourceJson({'id': 1}), isNull);
      expect(
        BakabaseApiClient.ratingFromResourceJson({
          'properties': {'2': <String, dynamic>{}},
        }),
        isNull,
      );
      expect(
        BakabaseApiClient.ratingFromResourceJson({
          'properties': {
            '2': {
              '13': {'values': <dynamic>[]},
            },
          },
        }),
        isNull,
      );
    });
  });

  test('play history entries parse', () {
    final entry = PlayHistoryEntry.fromJson({
      'id': 9,
      'resourceId': 42,
      'item': '/lib/a/ep1.mkv',
      'playedAt': '2026-08-29T10:00:00',
    });

    expect(entry.resourceId, 42);
    expect(entry.item, '/lib/a/ep1.mkv');
    expect(entry.playedAt, '2026-08-29T10:00:00');
  });
}
