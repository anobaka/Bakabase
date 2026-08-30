import 'dart:async';
import 'dart:io';

import 'package:bonsoir/bonsoir.dart';
import 'package:flutter/foundation.dart';

import 'discovered_server.dart';
import 'discovery_codec.dart';

/// Finds Bakabase servers on the local network and keeps a live, deduplicated
/// list (keyed by server id — the newest address wins).
///
/// Channels:
/// - mDNS via the platform NSD APIs (bonsoir) — works on iOS without any
///   entitlement, which sideloaded builds cannot hold.
/// - UDP broadcast probe — Android only: raw broadcast needs the multicast
///   entitlement on iOS. Covers networks that filter mDNS.
///
/// Both are best-effort; manual entry stays available regardless.
class DiscoveryService {
  DiscoveryService();

  final ValueNotifier<List<DiscoveredServer>> servers = ValueNotifier(const []);

  final Map<String, DiscoveredServer> _byId = {};
  BonsoirDiscovery? _mdns;
  StreamSubscription<BonsoirDiscoveryEvent>? _mdnsEvents;
  RawDatagramSocket? _probeSocket;
  Timer? _probeTimer;
  bool _running = false;

  Future<void> start() async {
    if (_running) {
      return;
    }
    _running = true;

    await _startMdns();
    _startUdpProbe();
  }

  Future<void> stop() async {
    _running = false;

    await _mdnsEvents?.cancel();
    _mdnsEvents = null;
    await _mdns?.stop();
    _mdns = null;

    _probeTimer?.cancel();
    _probeTimer = null;
    _probeSocket?.close();
    _probeSocket = null;
  }

  Future<void> _startMdns() async {
    try {
      final discovery = BonsoirDiscovery(type: DiscoveryCodec.mdnsServiceType);
      await discovery.ready;

      _mdnsEvents = discovery.eventStream?.listen((event) {
        final service = event.service;
        if (service == null) {
          return;
        }

        if (event.type == BonsoirDiscoveryEventType.discoveryServiceFound) {
          // Found is only a name; the address arrives once resolved.
          service.resolve(discovery.serviceResolver);
          return;
        }

        if (event.type == BonsoirDiscoveryEventType.discoveryServiceResolved &&
            service is ResolvedBonsoirService) {
          final host = service.host;
          if (host == null || host.isEmpty) {
            return;
          }

          final server = DiscoveryCodec.fromTxt(
            service.attributes,
            host,
            service.port,
          );
          if (server != null) {
            _add(server);
          }
        }
      });

      await discovery.start();
      _mdns = discovery;
    } catch (e) {
      // No mDNS (permission denied, emulator, unsupported) — the probe
      // channel and manual entry still work.
      debugPrint('mDNS discovery unavailable: $e');
    }
  }

  void _startUdpProbe() {
    // Raw broadcast is Android-only by design; see the class comment.
    if (kIsWeb || !Platform.isAndroid) {
      return;
    }

    RawDatagramSocket.bind(InternetAddress.anyIPv4, 0).then((socket) {
      if (!_running) {
        socket.close();
        return;
      }

      socket.broadcastEnabled = true;
      socket.listen((event) {
        if (event != RawSocketEvent.read) {
          return;
        }
        final datagram = socket.receive();
        if (datagram == null) {
          return;
        }
        final server = DiscoveryCodec.tryParseProbeResponse(
          datagram.data,
          datagram.address.address,
        );
        if (server != null) {
          _add(server);
        }
      });

      _probeSocket = socket;
      _sendProbe();
      _probeTimer = Timer.periodic(const Duration(seconds: 4), (_) => _sendProbe());
    }).catchError((Object e) {
      debugPrint('UDP probe unavailable: $e');
    });
  }

  void _sendProbe() {
    try {
      _probeSocket?.send(
        DiscoveryCodec.buildProbeRequest(),
        InternetAddress('255.255.255.255'),
        DiscoveryCodec.probePort,
      );
    } catch (e) {
      debugPrint('UDP probe send failed: $e');
    }
  }

  void _add(DiscoveredServer server) {
    _byId[server.id] = server;
    servers.value = _byId.values.toList()
      ..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  }

  void dispose() {
    stop();
    servers.dispose();
  }
}
