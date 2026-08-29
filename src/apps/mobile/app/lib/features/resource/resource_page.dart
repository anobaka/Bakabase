import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/api_client.dart';
import '../../core/models.dart';

/// Resource detail: cover and playable files. Playback lands in M2 (in-app
/// media_kit player + native-player handoff); until then each item offers its
/// raw stream URL for pasting into any player, and records play history.
class ResourcePage extends StatefulWidget {
  const ResourcePage({super.key, required this.api, required this.resource});

  final BakabaseApiClient api;
  final ResourceSummary resource;

  @override
  State<ResourcePage> createState() => _ResourcePageState();
}

class _ResourcePageState extends State<ResourcePage> {
  List<PlayableItem>? _items;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final items = await widget.api.playableItems(widget.resource.id);
      if (mounted) {
        setState(() => _items = items);
      }
    } on ApiException catch (e) {
      if (mounted) {
        setState(() => _error = e.message);
      }
    }
  }

  Future<void> _copyStreamUrl(PlayableItem item) async {
    final url = widget.api.rawFileUrl(item.key);
    await Clipboard.setData(ClipboardData(text: url));

    // Fire and forget, like the web's hand-off: history must not block the
    // user, and the copy already succeeded.
    widget.api.markPlayed(widget.resource.id, item: item.key).ignore();

    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Stream link copied — paste it into a player')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final resource = widget.resource;
    final coverPath = resource.covers.isNotEmpty ? resource.covers.first : resource.path;

    return Scaffold(
      appBar: AppBar(title: Text(resource.title)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Center(
            child: ClipRRect(
              borderRadius: BorderRadius.circular(12),
              child: CachedNetworkImage(
                imageUrl: widget.api.thumbnailUrl(coverPath, width: 640),
                height: 280,
                fit: BoxFit.contain,
                errorWidget: (context, url, error) => const SizedBox(
                  height: 120,
                  child: Icon(Icons.image_not_supported_outlined, size: 48),
                ),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Text(resource.title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 4),
          Text(
            resource.path,
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 16),
          Text('Playable files', style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 4),
          if (_error != null)
            Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error))
          else if (_items == null)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(16),
                child: CircularProgressIndicator(),
              ),
            )
          else if (_items!.isEmpty)
            const Text('No playable files were found in this resource.')
          else
            for (final item in _items!)
              Card(
                child: ListTile(
                  leading: const Icon(Icons.play_circle_outline),
                  title: Text(item.title),
                  trailing: const Icon(Icons.copy, size: 18),
                  onTap: () => _copyStreamUrl(item),
                ),
              ),
        ],
      ),
    );
  }
}
