/// Which of the two apps this build runs as. Set by the entrypoint
/// (`main_admin.dart` / `main_client.dart`) and read across `core/`.
enum AppFlavor { admin, client }
