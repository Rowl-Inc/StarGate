# StarGate [![Build status](https://ci.appveyor.com/api/projects/status/l1ew8x5xhawy9o78?svg=true)](https://ci.appveyor.com/project/ronin1/stargate)
StarGate is a MongoDB Oplog (binary operation log) analyzer that can be modify to perform triggers base on any type of MongoDB event.

This application was originally engineered to generate REST calls (web-hook like operations) base on MongoDB write operations (IE: format and POST the insert from this collection to a remote REST service).

The inspiration came from a FaceBook Engineering publication regarding their implementation of a similar inhouse system called < <a href="https://www.facebook.com/notes/facebook-engineering/wormhole-pubsub-system-moving-data-through-space-and-time/10151504075843920">WormHole</a>
