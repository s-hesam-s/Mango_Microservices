# Link health checks

`python scripts/check_link_health.py` checks local Markdown fragments offline. It follows the documented GitHub heading rules for ATX/Setext headings, duplicate slugs, Unicode, inline formatting and explicit HTML `id`/`name` anchors. It reads tracked Markdown outside vendor, wwwroot and node_modules; sparse documents can be read from the current commit. Inline, reference and angle-autolinks plus HTML href/src/srcset are inspected. Source examples in fenced/indented/inline code and HTML comments are excluded. This is a focused checker, not a complete CommonMark renderer. Non-document fragments and fragments on external URLs require manual review.

`python -m unittest discover -s tests -p test_link_health.py` tests anchor changes, duplicate numbering, parsing, HTTP response classification, redirects, public-address restrictions and credential-free transport using local fixtures and mock responses. No test needs Internet access.

The separate **External link monitor** workflow runs weekly and can be run manually. It is not a required merge check and does not run for pull requests. It uses `python scripts/check_link_health.py --external` with no token, cookie or authentication. A successful HTTP response only proves the resource responded, not that its content is correct or an external fragment exists.

- HEAD requests fall back to GET for 404/410/405/501; only a confirmed GET 404/410 fails the scheduled job.
- 401/403, 429, server errors and DNS/TLS/timeouts are reported as **review**, not healthy or permanently broken. Review and skipped results do not block merges.
- Redirects are bounded and checked again at every hop; HTTPS downgrades are blocked.
- All DNS answers must be public. Connections use a validated numeric IP while retaining standard TLS certificate verification and hostname SNI. Private/loopback/link-local addresses, local/example hosts, credentials in URLs and unusual ports are blocked.
- At most three requests run concurrently, with six-second socket timeouts, a four-minute submission budget and 200 unique URLs per run. A bounded GET sends a Range header and closes after response headers. Deferred URLs remain review items.
- Reports omit URL queries and userinfo. Full destinations are never passed to shell commands. The workflow does not write issues, change repository settings or deploy.

See the workflow job summary for non-successful destinations and review them before changing content. Confirmed failures use GitHub's normal Actions failure reporting; no separate messaging integration is configured.

Heading rules: [GitHub section links and custom anchors](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax#section-links).
