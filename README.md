# StarGate [![Build status](https://ci.appveyor.com/api/projects/status/l1ew8x5xhawy9o78?svg=true)](https://ci.appveyor.com/project/ronin1/stargate)
StarGate is a MongoDB Oplog analyzer that can be customized to perform triggers base on any type of MongoDB event (IE: CRUD ops, db admin ops). Think of it as an external trigger engine.

![](https://github.com/Rowl-Inc/StarGate/blob/master/wiki/img/diagram.png)

This application was originally engineered to generate REST calls on MongoDB write operations.  The inspiration came from a Facebook Engineering publication of a similar closed-sourced system developed for MySQL called <a href="https://www.facebook.com/notes/facebook-engineering/wormhole-pubsub-system-moving-data-through-space-and-time/10151504075843920">WormHole</a>.

## Structure & Components
StarGate is a windows service written in .NET 4.5.  It also be run as a standalone console application for debugging purposes.  The app requires proper configuration before it can be use.
The heart of StarGate is a modular, configurable core written using decorator pattern.  The idea is, smaller components that acts as filter, processor, and publisher can be developed in isolation, and stitched together using configuration file (or at run-time) to analyze mongo operation log and "do something" with it.  For now, we allow the "do something" logic to be written in JavaScript.  The reason for this is simple: MongoDB data is stored in BSON and is easily expressed in JSON, which is native to JavaScript.
What data to analyze and what to do with the data is highly configurable.  These are the main types of components that make up the configurable core:

### Route (Filter)
The job of the listener is to listen (filter) for a specific log entry that has been written to the operation log, then pass that long to the next component.  The route is the head of the chain of responsibility.
We have the following route types implemented:

* *Route by NameSpace:* filters data by mongo name space which are: database name, collection name & operation type.  Filter rules are given in regular expression.  If the input data's source (db, collection names) matches the regular expression and the operation type matches, pass it on to the next component in the chain.
* *Route by JavaScript Predicate:* filters data by a custom javascript functions that takes the mongo oplog object in json and returns a boolean value.  If the return value is true, the oplog data is passed along to the next component in the chain.  The javascript function is defined by the user of the system as part of StarGate configuration.

Note that multiple routes can be defined per instance of StarGate and each route has the option of passing along the oplog data to the next route defined once the data has matched the route or keeping the oplog data for itself.  Of course, if the oplog does not match a route, it always passes the oplog data along to the next route.

### Decorator (Processor)
Zero or multiple decorators can be chained together, there are no limits or in the order of chaining.  Here are some completed decorators:

* *Full Object Decorator:* for update operations in mongo, only the changes are reported by the oplog for efficiency reason.  This poses a problem if we want to send the entire updated object to ElasticSearch for example.  This decorator can be used to amend the entire updated object to the oplog context payload for further processing.
* *Call Once Decorator:* when an oplog context passes through this decorator, it will trigger a REST call the first and only time to a specific URL.  Verb, content type and actual content body can be customized.
* *Elastic Index Decorator:* is actually derived on the "Call Once Decorator" but it is specific to initiate & configure an elastic search index on first use.  If an index already exists, it will not amend or update the index and simply pass the oplog context to the next component in the chain.
* *Transform by JavaScript Decorator:* allow us to process the oplog context (either original oplog or previously computed result) and output a new result that might have a very different format.  For example: an input object may contains separate first and last name but an output object may combine these fields into a single full-name field.  The only option for this decorator is a boolean setting to "ignore null values."  When set, if the configured javascript transform method returns null, the chain will terminate and oplog data with null result will not get forwarded to the next component in the chain.

### Publisher (Trigger)
Publishers are at the tail-end of this chain of responsibility; they are the last operation and is meant to terminate the work at the end of the component chain.  These are the implemented triggers:

* *Null Publisher:* does nothing with the data.  However, an optional note can be provided and it will get logged along with the data if the proper logging configuration is implemented.
* *REST Publisher:* will take the computed result JSON and send it as a REST request body to a remote endpoint.  Endpoint URL and verb is configurable.  Content type is not since it is always JSON.
* *Dynamic REST (JavaScript) Publisher:* is derived from REST Publisher and full-fill a similar function.  However, instead of configuring a fixed endpoint URL and verb, a simple JavaScript function is used.  The input (as always) is the oplog context and the result is a simple JSON with 2 fields: URL, Verb.  This publisher will send the computed result payload (within the oplog context) to the specified URL address using the provided verb.  If null is returned, no request will be made and the component chain terminates.
* *Elastic Search Publisher:* is loosely derived from REST Publisher but is specifically designed to work with ElasticSearch.  The only required parameter is: Endpoint (URL).  The rest are truely optional and are use to ElasticSearch routing and or setting parent-child relationships if the data requires such structures.

## Example usecases would be: 
* Format and POST the inserts into a specific collection to a specific remote REST
* Evaluate collection drop statements and trigger a web-hook with the dropped collection data
* Sync data between MongoDB & ElasticSearch and format the JSON data using JavaScript functions between the transfers
* On document update to any collection with name matching a custom regular expression, send a REST PUT with the changes to a base endpoint with the full collection name appened
