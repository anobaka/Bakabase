import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// A server the app has successfully connected to before, keyed by the
/// server's persistent id so an IP change does not orphan the entry.
class ServerProfile {
  const ServerProfile({
    required this.id,
    required this.name,
    required this.baseUrl,
    required this.lastConnectedAt,
  });

  final String id;
  final String name;

  /// Last address that worked, e.g. `http://192.168.1.5:34567`.
  final String baseUrl;
  final DateTime lastConnectedAt;

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'baseUrl': baseUrl,
        'lastConnectedAt': lastConnectedAt.toIso8601String(),
      };

  static ServerProfile? fromJson(Map<String, dynamic> json) {
    final id = json['id'] as String?;
    final baseUrl = json['baseUrl'] as String?;
    if (id == null || id.isEmpty || baseUrl == null || baseUrl.isEmpty) {
      return null;
    }
    return ServerProfile(
      id: id,
      name: json['name'] as String? ?? 'Bakabase',
      baseUrl: baseUrl,
      lastConnectedAt:
          DateTime.tryParse(json['lastConnectedAt'] as String? ?? '') ??
              DateTime.fromMillisecondsSinceEpoch(0),
    );
  }
}

/// Persistence for [ServerProfile]s, newest connection first.
class ServerProfileStore {
  static const _key = 'bakabase.serverProfiles';

  /// Pure merge logic, split out for testing: replaces any profile with the
  /// same id and sorts by recency.
  static List<ServerProfile> merge(List<ServerProfile> existing, ServerProfile updated) {
    final result = [
      updated,
      ...existing.where((p) => p.id != updated.id),
    ]..sort((a, b) => b.lastConnectedAt.compareTo(a.lastConnectedAt));
    return result;
  }

  Future<List<ServerProfile>> load() async {
    final prefs = await SharedPreferences.getInstance();
    final raw = prefs.getString(_key);
    if (raw == null) {
      return const [];
    }

    try {
      final list = jsonDecode(raw);
      if (list is! List) {
        return const [];
      }
      return list
          .whereType<Map<String, dynamic>>()
          .map(ServerProfile.fromJson)
          .whereType<ServerProfile>()
          .toList();
    } on FormatException {
      return const [];
    }
  }

  Future<List<ServerProfile>> save(ServerProfile profile) async {
    final merged = merge(await load(), profile);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key, jsonEncode(merged.map((p) => p.toJson()).toList()));
    return merged;
  }

  Future<List<ServerProfile>> remove(String id) async {
    final remaining = (await load()).where((p) => p.id != id).toList();
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_key, jsonEncode(remaining.map((p) => p.toJson()).toList()));
    return remaining;
  }
}
