import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/connection.dart';
import '../../l10n/app_localizations.dart';
import '../../discovery/discovered_server.dart';
import '../../discovery/discovery_service.dart';

/// The front door: pick a discovered server, a remembered one, or type an
/// address. Discovery runs for as long as this page is visible.
class ConnectPage extends ConsumerStatefulWidget {
  const ConnectPage({super.key});

  @override
  ConsumerState<ConnectPage> createState() => _ConnectPageState();
}

class _ConnectPageState extends ConsumerState<ConnectPage> {
  final DiscoveryService _discovery = DiscoveryService();
  final TextEditingController _manualAddress = TextEditingController();

  @override
  void initState() {
    super.initState();
    _discovery.start();
  }

  @override
  void dispose() {
    _discovery.dispose();
    _manualAddress.dispose();
    super.dispose();
  }

  void _connect(String baseUrl) {
    ref.read(connectionProvider.notifier).connect(baseUrl);
  }

  void _connectManual() {
    var input = _manualAddress.text.trim();
    if (input.isEmpty) {
      return;
    }
    if (!input.startsWith('http://') && !input.startsWith('https://')) {
      input = 'http://$input';
    }
    _connect(input);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final connection = ref.watch(connectionProvider);
    final profiles = ref.watch(serverProfilesProvider);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.connectTitle)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (connection is Connecting)
            ListTile(
              leading: const SizedBox(
                width: 24,
                height: 24,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
              title: Text(l10n.connecting),
            ),
          if (connection is ConnectionFailed) _FailureCard(failure: connection),
          _SectionHeader(
            title: l10n.onThisNetwork,
            trailing: const SizedBox(
              width: 14,
              height: 14,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          ),
          ValueListenableBuilder<List<DiscoveredServer>>(
            valueListenable: _discovery.servers,
            builder: (context, servers, _) {
              if (servers.isEmpty) {
                return Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Text(l10n.discoveryHint),
                );
              }
              return Column(
                children: [
                  for (final server in servers)
                    Card(
                      child: ListTile(
                        leading: const Icon(Icons.dns_outlined),
                        title: Text(server.name),
                        subtitle: Text(
                          '${server.baseUrl}'
                          '${server.appVersion.isNotEmpty ? ' · v${server.appVersion}' : ''}',
                        ),
                        onTap: () => _connect(server.baseUrl),
                      ),
                    ),
                ],
              );
            },
          ),
          const SizedBox(height: 16),
          _SectionHeader(title: l10n.remembered),
          profiles.when(
            data: (list) => list.isEmpty
                ? Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    child: Text(l10n.rememberedEmpty),
                  )
                : Column(
                    children: [
                      for (final profile in list)
                        Card(
                          child: ListTile(
                            leading: const Icon(Icons.history),
                            title: Text(profile.name),
                            subtitle: Text(profile.baseUrl),
                            onTap: () => _connect(profile.baseUrl),
                          ),
                        ),
                    ],
                  ),
            loading: () => const SizedBox.shrink(),
            error: (_, _) => const SizedBox.shrink(),
          ),
          const SizedBox(height: 16),
          _SectionHeader(title: l10n.byAddress),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _manualAddress,
                  decoration: const InputDecoration(
                    hintText: '192.168.1.5:34567',
                    border: OutlineInputBorder(),
                    isDense: true,
                  ),
                  keyboardType: TextInputType.url,
                  onSubmitted: (_) => _connectManual(),
                ),
              ),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: _connectManual,
                child: Text(l10n.connect),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader({required this.title, this.trailing});

  final String title;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          Text(title, style: Theme.of(context).textTheme.titleSmall),
          if (trailing != null) ...[const SizedBox(width: 8), trailing!],
        ],
      ),
    );
  }
}

class _FailureCard extends StatelessWidget {
  const _FailureCard({required this.failure});

  final ConnectionFailed failure;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final hint = failure.denial == RemoteAccessDenial.disabled
        ? l10n.remoteAccessDisabledHint
        : switch (failure.kind) {
            ConnectionFailureKind.protocolTooNew => l10n.protocolTooNew(failure.detail),
            ConnectionFailureKind.protocolTooOld => l10n.protocolTooOld(failure.detail),
            ConnectionFailureKind.network => failure.detail,
          };

    return Card(
      color: Theme.of(context).colorScheme.errorContainer,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.couldNotConnect(failure.baseUrl),
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: 4),
            Text(hint),
          ],
        ),
      ),
    );
  }
}
