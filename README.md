# StarGate
StarGate is a MongoDB Oplog (binary operation log) analyzer that can be customized to perform triggers base on any type of MongoDB event (IE: CRUD ops, db admin ops).  Think of it as a MongoDB external trigger engine.

This application was originally engineered to generate REST calls (web-hook like operations) base on MongoDB write operations. 

Example usecases would be: 
* Format and POST the inserts into a specific collection to a specific remote REST
* Evaluate collection drop statements and trigger a web-hook with the dropped collection data
* Sync data between MongoDB & ElasticSearch and format the JSON data using JavaScript functions between the transfers

The inspiration came from a FaceBook Engineering publication regarding their implementation of a similar inhouse system called  <a href="https://www.facebook.com/notes/facebook-engineering/wormhole-pubsub-system-moving-data-through-space-and-time/10151504075843920">WormHole</a>
