/// Hand-written models for the handful of endpoints the thin client uses.
/// Field names mirror the server's camelCase JSON. When the endpoint surface
/// grows past this, switch to the generated client planned in
/// docs/mobile-app-design.md (S5) instead of growing this file forever.
library;

/// `GET /remote-access/server-info` and the discovery beacon carry the same
/// facts; both are parsed into this.
class ServerInfo {
  const ServerInfo({
    required this.id,
    required this.name,
    required this.appVersion,
    required this.protocolVersion,
    this.mode,
  });

  final String id;
  final String name;
  final String appVersion;
  final int protocolVersion;

  /// RemoteAccessMode: 0 disabled, 1 enabled, 2 unrestricted. Absent in
  /// discovery payloads.
  final int? mode;

  static ServerInfo fromJson(Map<String, dynamic> json) => ServerInfo(
        id: json['id'] as String,
        name: json['name'] as String? ?? 'Bakabase',
        appVersion: json['appVersion'] as String? ?? '',
        protocolVersion: (json['protocolVersion'] as num?)?.toInt() ?? 0,
        mode: (json['mode'] as num?)?.toInt(),
      );
}

class MediaLibrary {
  const MediaLibrary({
    required this.id,
    required this.name,
    required this.resourceCount,
    this.color,
  });

  final int id;
  final String name;
  final int resourceCount;
  final String? color;

  static MediaLibrary fromJson(Map<String, dynamic> json) => MediaLibrary(
        id: (json['id'] as num).toInt(),
        name: json['name'] as String? ?? '',
        resourceCount: (json['resourceCount'] as num?)?.toInt() ?? 0,
        color: json['color'] as String?,
      );
}

class ResourceSummary {
  const ResourceSummary({
    required this.id,
    required this.path,
    this.displayName,
    this.fileName,
    this.isFile = false,
    this.playedAt,
    this.covers = const [],
  });

  final int id;
  final String path;
  final String? displayName;
  final String? fileName;
  final bool isFile;
  final String? playedAt;

  /// Cover file paths (server-side paths), present when the search asked for
  /// the Cover additional item. Feed them to the thumbnail endpoint.
  final List<String> covers;

  String get title =>
      displayName?.trim().isNotEmpty == true ? displayName!.trim() : (fileName ?? path);

  static ResourceSummary fromJson(Map<String, dynamic> json) => ResourceSummary(
        id: (json['id'] as num).toInt(),
        path: json['path'] as String? ?? '',
        displayName: json['displayName'] as String?,
        fileName: json['fileName'] as String?,
        isFile: json['isFile'] as bool? ?? false,
        playedAt: json['playedAt'] as String?,
        covers: (json['covers'] as List<dynamic>? ?? const [])
            .whereType<String>()
            .toList(),
      );
}

class SearchResult {
  const SearchResult({
    required this.resources,
    required this.totalCount,
    required this.page,
    required this.pageSize,
  });

  final List<ResourceSummary> resources;
  final int totalCount;
  final int page;
  final int pageSize;

  bool get hasMore => page * pageSize < totalCount;
}

class PlayableItem {
  const PlayableItem({required this.key, this.displayName});

  /// For filesystem items this is the file's full path on the server.
  final String key;
  final String? displayName;

  String get title {
    if (displayName?.trim().isNotEmpty == true) {
      return displayName!.trim();
    }
    final segments = key.split(RegExp(r'[/\\]'));
    return segments.isNotEmpty ? segments.last : key;
  }

  static PlayableItem fromJson(Map<String, dynamic> json) => PlayableItem(
        key: json['key'] as String? ?? '',
        displayName: json['displayName'] as String?,
      );
}
