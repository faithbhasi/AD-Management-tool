#!/usr/bin/env python3
"""Smoke test for a freshly started local ILM Portal (Development, fictional data; start with an empty App_Data).

Signs in through the development mock OIDC provider as fictional operators, then drives a
leaver through request, approval by a second operator, containment and SafelyContained.
Only for https://localhost with the ASP.NET Core development certificate.
"""
import html, http.cookiejar, re, ssl, sys, urllib.parse, urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "https://localhost:5001"
assert urllib.parse.urlparse(BASE).hostname in ("localhost", "127.0.0.1"), "smoke test is for local development only"
CTX = ssl.create_default_context()
CTX.check_hostname = False
CTX.verify_mode = ssl.CERT_NONE  # local development certificate only


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def session():
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=CTX), urllib.request.HTTPCookieProcessor(jar), NoRedirect)
    return opener


def req(opener, method, url, data=None):
    if not url.startswith("http"):
        url = BASE + url
    body = urllib.parse.urlencode(data).encode() if data is not None else None
    r = urllib.request.Request(url, data=body, method=method)
    try:
        resp = opener.open(r)
        return resp.status, resp.headers, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.headers, e.read().decode("utf-8", "replace")


def sign_in(subject):
    s = session()
    code, headers, _ = req(s, "GET", "/Account/SignIn")
    assert code == 302, f"SignIn -> {code}"
    authorize = headers["Location"]
    code, _, page = req(s, "GET", authorize)
    assert code == 200 and "Choose a fictional operator" in page, "mock authorize page"
    params = dict(urllib.parse.parse_qsl(urllib.parse.urlparse(authorize).query))
    params["subject"] = subject
    code, headers, _ = req(s, "POST", "/mock-oidc/authorize", params)
    assert code == 302, f"authorize POST -> {code}"
    code, headers, body = req(s, "GET", headers["Location"])
    assert code == 302, f"callback -> {code} {body[:200]}"
    code, _, page = req(s, "GET", "/")
    assert code == 200 and "Dashboard" in page, "dashboard after sign-in"
    return s


def token(page):
    m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', page)
    assert m, "antiforgery token"
    return m.group(1)


def main():
    results = []
    def check(name, ok, detail=""):
        results.append((name, ok, detail))
        print(("PASS " if ok else "FAIL ") + name + (f" ({detail})" if detail else ""))

    code, _, body = req(session(), "GET", "/health/live")
    check("GET /health/live", code == 200)
    code, _, body = req(session(), "GET", "/health/ready")
    check("GET /health/ready", code == 200, body.strip())
    code, headers, _ = req(session(), "GET", "/")
    location = headers.get("Location", "")
    check("anonymous request is challenged via OIDC", code == 302 and ("/mock-oidc/authorize" in location or "/Account/SignIn" in location), location[:80])

    olivia = sign_in("00uOperatorOlivia001")
    check("development login (Olivia Operator)", True)
    code, _, page = req(olivia, "GET", "/Account/Me")
    check("roles resolved server-side", "LifecycleOperator" in page and "okta-group-membership" in page)

    mallory = sign_in("00uDecoyMallory00001")
    code, _, page = req(mallory, "GET", "/Account/Me")
    check("renamed group name grants no role", "No roles." in page)
    code, headers, _ = req(mallory, "GET", "/Audit")
    check("unprivileged user denied audit", code in (302, 403) and "AccessDenied" in headers.get("Location", "AccessDenied"))

    code, _, page = req(olivia, "GET", "/Users?q=alex")
    check("user search", code == 200 and "alex.example" in page)
    code, _, page = req(olivia, "GET", "/Computers?q=DC01")
    code2, _, page2 = req(olivia, "GET", "/Computers?q=WS")
    check("computer search is scope-limited (DC01 hidden, WS visible)", ">DC01</a>" not in page and ">WS-0001</a>" in page2)

    code, _, people = req(olivia, "GET", "/People?q=Casey")
    m = re.search(r'/People/Details/([0-9a-f-]{36})', people)
    check("person search", m is not None)
    person_id = m.group(1)
    code, _, page = req(olivia, "GET", f"/Leavers/New?personId={person_id}")
    check("leaver plan preview", code == 200 and "ContainmentOnlyLegacy" in page)
    code, headers, _ = req(olivia, "POST", f"/Leavers/New?personId={person_id}", {
        "__RequestVerificationToken": token(page), "Reason": "Smoke test leaver (fictional)", "TicketReference": "CHG-90001",
        "Urgency": "Urgent", "EffectiveUtc": "", "IdempotencyKey": "smoke-" + person_id})
    check("leaver requested", code == 302 and "/Leavers/Details/" in headers.get("Location", ""))
    leaver_url = headers["Location"]

    code, _, page = req(olivia, "GET", leaver_url)
    check("requester cannot see approve button", "Approve plan" not in page)

    sasha = sign_in("00uSecuritySasha0001")
    code, _, page = req(sasha, "GET", leaver_url)
    check("security approver sees approval", "Approve plan" in page)
    code, headers, _ = req(sasha, "POST", leaver_url + "?handler=Approve", {"__RequestVerificationToken": token(page), "comment": "approved in smoke test"})
    code, _, page = req(sasha, "GET", leaver_url)
    check("approved", ">Approved<" in page or "Approved" in page)

    code, _, page = req(olivia, "GET", leaver_url)
    code, headers, _ = req(olivia, "POST", leaver_url + "?handler=Start", {"__RequestVerificationToken": token(page)})
    code, _, page = req(olivia, "GET", leaver_url)
    state = re.search(r'<span class="badge [a-z]+">([A-Za-z]+)</span></h1>', page)
    # The background worker may already have advanced it to the first non-urgent stage.
    check("legacy-only leaver SafelyContained (no target identity)", state is not None and state.group(1) in ("SafelyContained", "RetentionActionsPending"), state.group(1) if state else "no state")

    auditor = sign_in("00uAuditorAudrey0001")
    code, _, page = req(auditor, "GET", "/Audit")
    check("auditor reads audit", code == 200 and "LeaverStateChanged" in page)
    code, _, page = req(auditor, "POST", "/Audit?handler=Verify", {"__RequestVerificationToken": token(page)})
    check("audit chain verifies", "Chain valid" in page)
    code, headers, csv = req(auditor, "GET", "/Audit?handler=Export")
    check("audit CSV export", code == 200 and csv.startswith('"Sequence"'))

    failed = [r for r in results if not r[1]]
    print(f"\n{len(results) - len(failed)}/{len(results)} smoke checks passed")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
