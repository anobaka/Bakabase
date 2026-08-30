import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../core/api_client.dart';

/// Horizontal page-flip reader for a resource's image files — the
/// comic/gallery case. Images stream straight from the server (raw for plain
/// files, the extracting endpoint for archive entries); adjacent pages are
/// pre-fetched so a steady reading pace never waits.
class ReaderPage extends StatefulWidget {
  const ReaderPage({
    super.key,
    required this.api,
    required this.title,
    required this.imagePaths,
    this.initialIndex = 0,
  });

  final BakabaseApiClient api;
  final String title;

  /// Server-side paths, in reading order.
  final List<String> imagePaths;
  final int initialIndex;

  @override
  State<ReaderPage> createState() => _ReaderPageState();
}

class _ReaderPageState extends State<ReaderPage> {
  late final PageController _pages = PageController(initialPage: widget.initialIndex);
  late int _index = widget.initialIndex;
  bool _chromeVisible = true;

  String _urlAt(int index) => widget.api.streamUrl(widget.imagePaths[index]);

  void _precacheAround(int index) {
    for (final neighbor in [index - 1, index + 1, index + 2]) {
      if (neighbor >= 0 && neighbor < widget.imagePaths.length) {
        precacheImage(CachedNetworkImageProvider(_urlAt(neighbor)), context);
      }
    }
  }

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: _chromeVisible
          ? AppBar(
              backgroundColor: Colors.black54,
              foregroundColor: Colors.white,
              title: Text(
                '${widget.title} · ${_index + 1}/${widget.imagePaths.length}',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            )
          : null,
      body: GestureDetector(
        onTap: () => setState(() => _chromeVisible = !_chromeVisible),
        child: PageView.builder(
          controller: _pages,
          itemCount: widget.imagePaths.length,
          onPageChanged: (index) {
            setState(() => _index = index);
            _precacheAround(index);
          },
          itemBuilder: (context, index) => InteractiveViewer(
            maxScale: 5,
            child: Center(
              child: CachedNetworkImage(
                imageUrl: _urlAt(index),
                fit: BoxFit.contain,
                placeholder: (context, url) =>
                    const CircularProgressIndicator(strokeWidth: 2),
                errorWidget: (context, url, error) => const Icon(
                  Icons.broken_image_outlined,
                  color: Colors.white54,
                  size: 48,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
