# Winche Rules — Language Guide

Winche rules decide who may read and write your data. They're written in a small language close to Firebase's
security rules, kept in `database.rules` and `storage.rules`, and enforced by the server on every request.

This guide is about **writing rules**. For the .NET engine API, see the [README](README.md).

- [A first ruleset](#a-first-ruleset)
- [How rules are applied](#how-rules-are-applied)
- [Paths and captures](#paths-and-captures)
- [Operations](#operations)
- [What a condition can use](#what-a-condition-can-use)
- [Values](#values)
- [Operators](#operators)
- [Functions](#functions)
- [Writing your own functions](#writing-your-own-functions)
- [Common patterns](#common-patterns)
- [Queries: rules are not filters](#queries-rules-are-not-filters)
- [When a condition fails to evaluate](#when-a-condition-fails-to-evaluate)
- [Testing rules](#testing-rules)
- [Deploying rules](#deploying-rules)
- [Coming from Firebase](#coming-from-firebase)
- [Reference](#reference)

---

## A first ruleset

```text
// database.rules
match users/{uid} {
  // Anyone signed in can read a profile; only its owner can change it.
  allow get: if request.auth != null;
  allow create: if request.auth.uid == uid
    && request.resource.data has only ['name', 'email']
    && request.resource.data.name is string;
  allow update: if request.auth.uid == uid && changes only ['name'];

  match notes/{noteId} {
    // A user's notes are private to them.
    allow read, write: if request.auth.uid == uid;
  }
}
```

Reading it:

- `match users/{uid}` applies to every document in `users`; `{uid}` captures its id, which the conditions use.
- `allow get: if …` grants **reading one document** when the condition is true.
- `create` requires the new document to contain only `name` and `email`, with `name` a string.
- `update` only lets the owner change `name`.
- `match notes/{noteId}` is nested, so it covers `users/{uid}/notes/{noteId}` and can use `uid` too.

---

## How rules are applied

1. **Nothing is allowed by default.** A request is denied unless a rule allows it. An empty file allows nothing.
2. **Rules only grant.** There is no `deny`. If *any* matching `allow` grants the operation, the request is
   allowed — rules are OR-ed together, and their order doesn't matter.
3. **A rule applies to the path it matches.** `match users/{uid}` covers `users/u1`, not `users/u1/notes/n1`
   (use a nested match or a recursive capture for that).

Because one grant is enough, **grant narrowly**: a broad `allow read: if true;` on `{path=**}` can't be narrowed
by a stricter rule elsewhere.

---

## Paths and captures

A `match` path is the document path without a leading `/`, made of segments:

| Segment | Matches | Example |
| --- | --- | --- |
| `users` | exactly that segment | `match users/admin` |
| `{uid}` | any one segment, captured as `uid` | `match users/{uid}` |
| `{rest=**}` at the end | one or more segments, captured as a path | `match files/{rest=**}` — `files/a`, `files/a/b/c` |
| `{path=**}` at the start or middle | zero or more segments | `match {path=**}/comments/{id}` — `comments` under any parent |

- **Nested matches** extend their parent's path, and see the parent's captures.
- **One recursive capture per path**, counting the parents. A match that ends in `{x=**}` can't have nested
  matches (they could never be reached).
- **Collection group queries** (every `comments` collection, under any parent) are governed by rules that cover
  every parent: `match {path=**}/comments/{id}`.

---

## Operations

| Write | Covers |
| --- | --- |
| `get` | reading one document |
| `list` | a query over a collection |
| `create` | writing a document that doesn't exist |
| `update` | changing a document that exists |
| `delete` | removing a document |
| `read` | `get` + `list` |
| `write` | `create` + `update` + `delete` |

Several at once: `allow read, create: if …;`. With no condition — `allow get;` — the rule always grants.

---

## What a condition can use

### `request`

| | |
| --- | --- |
| `request.auth` | the caller, or `null` when signed out |
| `request.auth.uid` | the caller's `uid` claim |
| `request.auth.token.<claim>` | any claim from the caller's token: `request.auth.token.email`, `.sub`, `.role`… |
| `request.resource` | the document **as it will be** after a create or update (`null` for reads and deletes) |
| `request.time` | when the request is evaluated (for writes, the commit time) |
| `request.method` | `get`, `list`, `create`, `update` or `delete` |

Which claims exist depends on how the app maps the caller's token; `token.sub` (the user id in Keycloak) is the
usual one.

### `resource`

The document **as it is now** — for `get`, `update` and `delete` (`null` for `create`; for `list`, see
[Queries](#queries-rules-are-not-filters)).

In **database** rules a document has:

| | |
| --- | --- |
| `resource.name`, `resource.address.city` | its fields, directly |
| `resource.data` | its fields alone, as a map — for `has only`, `keys()`, `changes`, `diff()` |
| `resource.id` | its id (the last path segment) |
| `resource.path`, `resource.collection` | its full path, and the id of its collection |
| `resource.createdAt`, `resource.updatedAt` | timestamps |
| `resource.version` | goes up by one on every write |

`request.resource` has the same shape. A field named like one of these columns is hidden by the column — read it
through `resource.data`.

In **storage** rules a file has `path`, `directory`, `id`, `mimeType`, `sizeBytes`, `metadata` (its custom metadata,
a map), `uploadStatus`, `version`, `createdAt` and `updatedAt`.

### Path captures

Every `{name}` in the enclosing matches is a variable: in `match users/{uid}/notes/{noteId}`, conditions can use
`uid` and `noteId`.

---

## Values

| Kind | Write |
| --- | --- |
| strings | `'text'` or `"text"` (`\'`, `\\`, `\n` escapes) |
| numbers | `42`, `-3`, `1.5`, `1e3` |
| booleans, null | `true`, `false`, `null` |
| lists | `['admin', 'staff']` |
| maps | `{'a': 1, 'b': [true, null]}` |
| timestamps | `timestamp('2027-01-01T00:00:00Z')` |
| paths | `/teams/t1`, `/users/$(request.auth.uid)` — see [Building paths](#building-paths) (or `path('teams/t1')`) |
| bytes | `bytes('AAEC')` (base64) |
| geo points | no literal; a stored location is a `latlng` — read `.latitude` and `.longitude` (a map that merely has those keys is not a `latlng`) |

A field whose name isn't a plain identifier goes in backticks: ``resource.`first name` ``.

Comments start with `//` and run to the end of the line.

---

## Operators

Loosest first:

| Operator | Meaning |
| --- | --- |
| `c ? a : b` | `a` if `c`, else `b` |
| `a \|\| b` | either |
| `a && b` | both |
| `==` `!=` `<` `<=` `>` `>=` | comparisons |
| `x in list`, `key in map` | membership |
| `x not in list` | not a member — same as `!(x in list)` |
| `x is string` | type check: `string int float number bool null map list timestamp bytes path latlng` |
| `map has only [...]` | the map has no keys but these |
| `map has all [...]` / `has any [...]` | the map has every / at least one of these keys |
| `changes only [...]` | an update changes no fields but these |
| `changes all [...]` / `changes any [...]` | an update changes every / at least one of these fields |
| `!a` | not |
| `a.b`, `a[i]`, `a.f(x)` | field, index, method |

Group with parentheses when in doubt: `(a || b) && c`.

`has` is about a **map's keys** — a document's fields, a token's claims. For a list, use `x in list` or the
`hasOnly()` / `hasAll()` / `hasAny()` methods.

`changes …` compares the document after the update with the one before
(`request.resource.data` against `resource.data`); use it in `update` rules.

Numbers compare by value across ints and floats (`1 == 1.0`). Values of different kinds (a string and a number)
are never equal — so `!=` between them is true — and ordering them (`<`) is an error.

---

## Functions

Methods can also be written as functions: `x.size()` is `size(x)`.

| Function | Gives |
| --- | --- |
| `exists(path)` | whether a document exists |
| `get(path)` | another document (same shape as `resource`) |
| `x.size()` | length of a string, list or map |
| `m.keys()` | a map's keys, as a list |
| `l.hasOnly([...])` | every element of `l` is one of these |
| `l.hasAll([...])` | `l` contains every one of these |
| `l.hasAny([...])` | `l` contains at least one of these |
| `after.diff(before)` | how two maps differ; then `.affectedKeys()`, `.addedKeys()`, `.removedKeys()`, `.changedKeys()`, `.unchangedKeys()` |
| `s.matches('regex')` | the whole string matches (100 ms limit; an invalid regex fails) |
| `s.lower()` / `s.upper()` | case conversion |
| `m.get('key', default)` | the value at a key, or `default` when it's missing |

### Building paths

`get()` and `exists()` take a path. Write one like a `match` path with a leading `/`, and put values in it with
`$( )`:

```text
get(/settings/app).maintenance == false
get(/users/$(request.auth.uid)).role == 'admin'
exists(/projects/$(projectId)/members/$(request.auth.uid))
```

Each `$( )` is **one segment**: a string or a number (a value containing `/` is an error, so an id can't reach
another path), or a path value — such as a `{rest=**}` capture — which adds all its segments. Firebase's
`/databases/$(database)/documents/` prefix is accepted and ignored, so its rules work as they are. Paths can also
be compared: `resource.team == /teams/$(teamId)`.

`path('users', uid, 'notes')` builds the same path from parts.

A call to a function that doesn't exist (usually a typo) always fails; the editors and `winche rules validate`
warn about it.

---

## Writing your own functions

A condition used in several rules can be named once:

```text
function signedIn() {
  return request.auth != null;
}

function isOwner(uid) {
  return signedIn() && request.auth.uid == uid;
}

match users/{uid} {
  function isStaff() {
    let role = request.auth.token.role;
    return role in ['admin', 'staff'];
  }

  allow get: if isOwner(uid) || isStaff();
  allow update: if isOwner(uid) && changes only ['name'];

  match notes/{noteId} {
    allow read, write: if isOwner(uid) || isStaff();
  }
}
```

- A function has parameters, any number of `let name = value;` lines, and one `return`.
- It can use `request`, `resource`, the path captures where it's defined, other functions, and the built-ins.
- **Where it's visible:** a top-level function works everywhere; one inside a `match` works in that block and
  the blocks nested in it. Order doesn't matter — call a function before or after it's defined.
- A call means exactly the same as writing the function's body there, so functions also work in `list` rules:
  `allow list: if isOwnedBy(request.auth.uid);` with `function isOwnedBy(u) { return resource.ownerId == u; }`
  is proved like the condition written out.
- Functions can't call themselves (directly or through others), and can't use a built-in's name. A call with the
  wrong number of arguments is an error in the file.

## Common patterns

**Signed in**
```text
allow read: if request.auth != null;
```

**Owner only** — the document's id is the user's id
```text
match users/{uid} {
  allow read, write: if request.auth.uid == uid;
}
```

**Owner only** — the owner is stored on the document
```text
match notes/{id} {
  allow get, update, delete: if resource.ownerId == request.auth.uid;
  allow create: if request.resource.ownerId == request.auth.uid;
  allow list: if resource.ownerId == request.auth.uid;   // queries must filter: where ownerId == <uid>
}
```

**Roles from token claims**
```text
allow write: if request.auth.token.role in ['admin', 'editor'];
allow delete: if request.auth.token has any ['admin'];      // the token has an `admin` claim
```

**Only known fields, with types and sizes**
```text
allow create: if request.resource.data has only ['title', 'body', 'tags']
  && request.resource.data has all ['title']
  && request.resource.data.title is string
  && request.resource.data.title.size() <= 100
  && (!('tags' in request.resource.data) || request.resource.data.tags is list);
```

**Some fields can't change**
```text
allow update: if changes only ['title', 'body'];               // everything else stays as it was
allow update: if !(changes any ['ownerId', 'createdBy']);      // these two never change
```

**Write once**
```text
allow create: if request.auth != null;
// no update or delete rule: once written, it stays
```

**Based on another document**
```text
allow read: if get(/settings/app).maintenance == false;
allow write: if get(/projects/$(projectId)/members/$(request.auth.uid)).role == 'editor';
```

**Until a date**
```text
allow create: if request.time < timestamp('2027-01-01T00:00:00Z');
```

**Everything under a user**
```text
match userData/{uid}/{rest=**} {
  allow read, write: if request.auth.uid == uid;
}
```

**All but some collections**
```text
match userData/{uid}/{collection}/{doc} {
  allow write: if request.auth.uid == uid && collection not in ['analytics', 'billing'];
}
```

**Public files, private uploads** (storage)
```text
match public/{file=**} {
  allow read;
}
match uploads/{uid}/{file=**} {
  allow read, update, delete: if request.auth.uid == uid;
  allow create: if request.auth.uid == uid && request.resource.sizeBytes < 10000000;   // under 10 MB
}
```

---

## Queries: rules are not filters

A `list` rule doesn't filter results one by one. The server checks the **query itself**: it's allowed only if
the rules allow **every document the query could return**. Otherwise the whole query is refused.

```text
match notes/{id} {
  allow list: if resource.ownerId == request.auth.uid;
}
```

| Query (signed in as `u1`) | Result |
| --- | --- |
| `notes where ownerId == 'u1'` | allowed — every result is `u1`'s |
| `notes` (no filter) | refused — it could return others' notes |
| `notes where ownerId == 'u2'` | refused |

So write `list` rules in terms of fields the query filters on, and filter the same way in the app.

- A condition that doesn't depend on the documents — on `request`, path captures, literals and functions of them —
  is simply evaluated: `allow list: if request.auth.token.role == 'admin';`.
- Captures in the collection's path are known: `match users/{uid}/notes/{id} { allow list: if request.auth.uid == uid; }`
  allows listing `users/u1/notes` for `u1`.
- Comparisons the query pins (`==`) or matches exactly (`age >= 18` against a rule's `age >= 18`) can be proved, as
  can either side of an `||`.
- Some conditions can't be proved for a query and always refuse it: `get()` / `exists()`, `in`, `?:`, and
  `resource.data` (write `resource.ownerId`, not `resource.data.ownerId`, in list rules).

---

## When a condition fails to evaluate

Reading a missing field, reading anything of `null` (such as `request.auth.uid` when signed out, `resource.x` on
a create, or `request.resource.x` on a read or delete), ordering incompatible values or calling an unknown
function is an **error**. An error makes that
rule's whole condition **false** — the request is denied unless another rule allows it.

That's safe, but it can be surprising:

- `request.auth.uid == uid` is simply false when signed out — no need to check for `null` first.
- In `a || b`, if `a` fails, the condition is false even when `b` would be true. Put the part that can fail
  last, or check first: `!('tags' in request.resource.data) || request.resource.data.tags is list`.
- Two separate `allow` lines don't affect each other: one failing doesn't stop the other granting.
- To read an optional field, use `m.get('field', default)`: `request.resource.data.get('tags', []).size() <= 5`.

---

## Testing rules

Write checks next to the rules, in `database.rules.test`, and run them with `winche rules test` (also run before
every `winche rules deploy`, and live in VS Code):

```text
test "owners read and write their notes" {
  auth { uid: 'u1' }
  allow get users/u1/notes/n1
  allow create users/u1/notes/n1 with { text: 'hi' }
  deny get users/u2/notes/n1
}

test "profiles change only the name" {
  auth { uid: 'u1' }
  resource { name: 'Ann', email: 'ann@example.com' }
  allow update users/u1 with { name: 'Annie', email: 'ann@example.com' }
  deny update users/u1 with { name: 'Ann', email: 'new@example.com' }
}

test "lists must filter by owner" {
  auth { uid: 'u1' }
  allow list notes where ownerId == 'u1'
  deny list notes
}
```

See the [CLI](https://github.com/FlameOfUdun/winche-cli#testing-rules) for everything a test can set up.

---

## Deploying rules

- **The console's rules editor** — edit, simulate and save; every save is a version you can revert to.
- **The CLI** — keep the files in your repo and `winche rules deploy` (it validates and tests them first);
  `winche rules history` and `winche rules rollback` mirror the console's history.
- **In the app** — `UseRulesFile("database.rules")` loads the file as the starting rules; a version deployed
  through the console replaces it.

Editors: the [VS Code extension](https://github.com/FlameOfUdun/winche-vscode) colours, completes and checks
rules and tests as you type.

---

## Coming from Firebase

Most rules carry over almost unchanged. The differences:

| Firebase | Winche |
| --- | --- |
| `rules_version = '2'; service cloud.firestore { match /databases/{database}/documents { … } }` | just the `match` blocks, at the top level |
| `match /users/{uid}` | `match users/{uid}` (no leading `/`) |
| `resource.data.ownerId` | `resource.ownerId` — `resource.data.ownerId` works too, except in list rules |
| `get(/databases/$(database)/documents/teams/$(id))` | the same, or `get(/teams/$(id))` |
| `request.resource.data.keys().hasOnly([...])` | also `request.resource.data has only [...]` |
| `request.resource.data.diff(resource.data).affectedKeys().hasOnly([...])` | also `changes only [...]` |
| `!(x in y)` | also `x not in y` |
| `function isOwner() { … }`, `let` | the same |
| `request.auth.token.email_verified`, custom claims | the same, from your identity provider's token |

---

## Reference

```text
file      := (match | function)*
match     := 'match' path '{' (allow | match | function)* '}'
function  := 'function' name '(' (name (',' name)*)? ')' '{' ('let' name '=' condition ';')* 'return' condition ';' '}'
allow     := 'allow' operation (',' operation)* (':' 'if' condition)? ';'
operation := 'get' | 'list' | 'create' | 'update' | 'delete' | 'read' | 'write'
path      := segment ('/' segment)*
segment   := name | '{' name '}' | '{' name '=**}'
```

Conditions use the [operators](#operators), [functions](#functions) and [values](#values) above; a path value is
`'/' part ('/' part)*` where a part is a segment or `$(condition)`. Keywords:
`match allow if function let return in is true false null`, plus the pairs `not in`, `has only|all|any` and `changes only|all|any`
(alone, `not`, `has`, `changes`, `only`, `all` and `any` are ordinary names).
