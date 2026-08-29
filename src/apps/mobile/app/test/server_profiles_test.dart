import 'package:bakabase_mobile/core/server_profiles.dart';
import 'package:flutter_test/flutter_test.dart';

ServerProfile profile(String id, {String? url, DateTime? at}) => ServerProfile(
      id: id,
      name: 'Server $id',
      baseUrl: url ?? 'http://10.0.0.1:1',
      lastConnectedAt: at ?? DateTime(2026, 1, 1),
    );

void main() {
  test('merge replaces the entry with the same server id', () {
    final merged = ServerProfileStore.merge(
      [profile('a', url: 'http://old:1')],
      profile('a', url: 'http://new:2', at: DateTime(2026, 2, 1)),
    );

    expect(merged, hasLength(1));
    expect(merged.single.baseUrl, 'http://new:2');
  });

  test('merge keeps other servers and sorts by recency', () {
    final merged = ServerProfileStore.merge(
      [profile('a', at: DateTime(2026, 3, 1)), profile('b', at: DateTime(2026, 1, 1))],
      profile('c', at: DateTime(2026, 2, 1)),
    );

    expect(merged.map((p) => p.id).toList(), ['a', 'c', 'b']);
  });

  test('json round trip', () {
    final original = profile('a', url: 'http://192.168.1.5:34567', at: DateTime(2026, 5, 6));
    final restored = ServerProfile.fromJson(original.toJson());

    expect(restored, isNotNull);
    expect(restored!.id, original.id);
    expect(restored.name, original.name);
    expect(restored.baseUrl, original.baseUrl);
    expect(restored.lastConnectedAt, original.lastConnectedAt);
  });

  test('entries without an id or address are dropped', () {
    expect(ServerProfile.fromJson({'name': 'x'}), isNull);
    expect(ServerProfile.fromJson({'id': 'a'}), isNull);
  });
}
