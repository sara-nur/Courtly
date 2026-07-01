package com.courtly

import io.flutter.embedding.android.FlutterFragmentActivity

// flutter_stripe's PaymentSheet (F26) requires the host activity to be a
// FragmentActivity, so we extend FlutterFragmentActivity instead of the default
// FlutterActivity. Everything else is Flutter's standard embedding.
class MainActivity : FlutterFragmentActivity()
