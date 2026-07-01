// F27 — Client notifications UI. Realtime SignalR hub client abstraction.
//
// [NotificationHubClient] is a tiny interface so tests can inject a no-op fake
// (no real socket in tests). [SignalRNotificationHubClient] is the production
// implementation over `signalr_netcore`. Every signalr call is wrapped
// defensively: [connect] never throws — on failure it simply reports
// disconnected via [onDisconnected] and returns.

import 'package:flutter/foundation.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../domain/notification_models.dart';

/// Supplies the current raw JWT (no "Bearer " prefix). Returns null when the
/// user is signed out; the hub then appends `access_token=` to the WS query.
typedef AccessTokenFactory = Future<String?> Function();

/// Minimal contract the realtime controller depends on. Kept intentionally small
/// so a fake can implement it with two no-op methods.
abstract class NotificationHubClient {
  Future<void> connect();
  Future<void> disconnect();
}

/// Production hub client. Builds the [HubConnection] lazily on the first
/// [connect], registers the server-pushed `"notification"` handler, and mirrors
/// connection lifecycle events to [onConnected] / [onDisconnected].
class SignalRNotificationHubClient implements NotificationHubClient {
  SignalRNotificationHubClient({
    required this.hubUrl,
    required this.accessTokenFactory,
    required this.onNotification,
    this.onConnected,
    this.onDisconnected,
  });

  final String hubUrl;
  final AccessTokenFactory accessTokenFactory;
  final void Function(AppNotification) onNotification;
  final void Function()? onConnected;
  final void Function()? onDisconnected;

  HubConnection? _hub;

  @override
  Future<void> connect() async {
    try {
      _hub ??= _build();
      final hub = _hub;
      if (hub == null) {
        onDisconnected?.call();
        return;
      }
      // Don't restart an already-live connection.
      if (hub.state == HubConnectionState.Connected ||
          hub.state == HubConnectionState.Connecting) {
        return;
      }
      await hub.start();
      onConnected?.call();
    } catch (e) {
      // Never throw out of connect(); polling remains the fallback.
      debugPrint('SignalRNotificationHubClient.connect failed: $e');
      onDisconnected?.call();
    }
  }

  @override
  Future<void> disconnect() async {
    final hub = _hub;
    _hub = null;
    if (hub == null) return;
    try {
      await hub.stop();
    } catch (e) {
      debugPrint('SignalRNotificationHubClient.disconnect failed: $e');
    }
  }

  HubConnection _build() {
    final hub = HubConnectionBuilder()
        .withUrl(
          hubUrl,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => (await accessTokenFactory()) ?? '',
            transport: HttpTransportType.WebSockets,
            logMessageContent: false,
          ),
        )
        .withAutomaticReconnect()
        .build();

    hub.on('notification', _handleNotification);
    hub.onclose(({Exception? error}) => onDisconnected?.call());
    hub.onreconnecting(({Exception? error}) => onDisconnected?.call());
    hub.onreconnected(({String? connectionId}) => onConnected?.call());

    return hub;
  }

  void _handleNotification(List<Object?>? args) {
    try {
      if (args == null || args.isEmpty) return;
      final raw = args[0];
      if (raw is! Map) return;
      onNotification(AppNotification.fromJson(raw.cast<String, dynamic>()));
    } catch (e) {
      // A malformed push must never break the socket; polling covers the gap.
      debugPrint('SignalRNotificationHubClient push parse failed: $e');
    }
  }
}
