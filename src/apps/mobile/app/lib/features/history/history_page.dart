import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';
import '../resource/resource_page.dart';

/// Play history, newest first — including entries this very device created via
/// played-at reporting. Resources are batch-resolved per page for titles and
/// covers; entries whose resource is gone are shown greyed-out.
class HistoryPage extends StatefulWidget {
  const HistoryPage({super.key, required this.api});

  final BakabaseApiClient api;

  @override
  State<HistoryPage> createState() => _HistoryPageState();
}

class _HistoryPageState extends State<HistoryPage> {
  static const _pageSize = 50;

  final ScrollController _scroll = ScrollController();
  final List<PlayHistoryEntry> _entries = [];
  final Map<int, ResourceSummary> _resources = {};
  int _page = 0;
  int _totalCount = 0;
  bool _loading = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(() {
      if (_scroll.position.extentAfter < 400) {
        _loadNextPage();
      }
    });
    _loadNextPage();
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _loadNextPage() async {
    if (_loading || (_page > 0 && _entries.length >= _totalCount)) {
      return;
    }
    _loading = true;

    try {
      final (entries, totalCount) =
          await widget.api.playHistory(page: _page + 1, pageSize: _pageSize);
      final unknownIds = entries
          .map((e) => e.resourceId)
          .where((id) => !_resources.containsKey(id))
          .toSet()
          .toList();
      final resolved = await widget.api.resourcesByIds(unknownIds);
      if (!mounted) {
        return;
      }
      setState(() {
        _page += 1;
        _totalCount = totalCount;
        _entries.addAll(entries);
        for (final resource in resolved) {
          _resources[resource.id] = resource;
        }
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _error = e.message);
      }
    } finally {
      _loading = false;
    }
  }

  String _subtitle(PlayHistoryEntry entry) {
    final parts = <String>[];
    final playedAt = entry.playedAt;
    if (playedAt != null) {
      final local = DateTime.tryParse(playedAt)?.toLocal();
      if (local != null) {
        parts.add(local.toString().substring(0, 16));
      }
    }
    final item = entry.item;
    if (item != null && item.isNotEmpty) {
      parts.add(item.split(RegExp(r'[/\\]')).last);
    }
    return parts.join(' · ');
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Play history')),
      body: _error != null
          ? Center(
              child: Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            )
          : ListView.builder(
              controller: _scroll,
              itemCount: _entries.length,
              itemBuilder: (context, index) {
                final entry = _entries[index];
                final resource = _resources[entry.resourceId];
                final coverPath = resource == null
                    ? null
                    : (resource.covers.isNotEmpty ? resource.covers.first : resource.path);

                return ListTile(
                  leading: coverPath == null
                      ? const Icon(Icons.question_mark)
                      : ClipRRect(
                          borderRadius: BorderRadius.circular(4),
                          child: CachedNetworkImage(
                            imageUrl: widget.api.thumbnailUrl(coverPath, width: 96),
                            width: 40,
                            height: 56,
                            fit: BoxFit.cover,
                            errorWidget: (context, url, error) =>
                                const Icon(Icons.broken_image_outlined),
                          ),
                        ),
                  title: Text(
                    resource?.title ?? 'Resource #${entry.resourceId} (removed)',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  subtitle: Text(_subtitle(entry), maxLines: 1, overflow: TextOverflow.ellipsis),
                  enabled: resource != null,
                  onTap: resource == null
                      ? null
                      : () => Navigator.of(context).push(
                            MaterialPageRoute<void>(
                              builder: (_) =>
                                  ResourcePage(api: widget.api, resource: resource),
                            ),
                          ),
                );
              },
            ),
    );
  }
}
