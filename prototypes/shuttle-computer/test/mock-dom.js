// Minimal DOM, enough to mount and drive the UI headlessly.
//
// Not a browser — it proves the UI builds, wires up and responds to input
// without throwing. Layout and looks are Sam's job at the review gate.
//
// index.html is PARSED rather than hand-rebuilt here, so if the markup and the
// JS ever disagree about an id, this is where it surfaces.

class ClassList {
    constructor (el) { this.el = el; this.set = new Set (); }
    add (...c) { for (const x of c) if (x) this.set.add (x); this._sync (); }
    remove (...c) { for (const x of c) this.set.delete (x); this._sync (); }
    contains (c) { return this.set.has (c); }
    toggle (c, force) {
        const on = force === undefined ? !this.set.has (c) : !!force;
        if (on) this.set.add (c); else this.set.delete (c);
        this._sync ();
        return on;
    }
    _sync () { this.el._className = [...this.set].join (' '); }
    get value () { return [...this.set].join (' '); }
}

class El {
    constructor (doc, tag) {
        this.ownerDocument = doc;
        this.tagName = String (tag).toUpperCase ();
        this.childNodes = [];
        this.parentNode = null;
        this.attributes = {};
        this.listeners = {};
        this.style = { cssText: '', setProperty () {} };
        this._className = '';
        this._id = '';
        this._text = '';
        this.classList = new ClassList (this);
        this.tabIndex = -1;
        this.disabled = false;
    }

    get className () { return this._className; }
    set className (v) {
        this._className = v || '';
        this.classList.set = new Set (String (v || '').split (/\s+/).filter (Boolean));
    }

    get id () { return this._id; }
    set id (v) { this._id = v; if (v) this.ownerDocument._byId.set (v, this); }

    // Real textContent is the concatenation of every descendant's text, not
    // just this node's own. Tests that ask a container what it says depend on
    // that, so the mock has to do it too.
    get textContent () {
        let s = this._text;
        for (const c of this.childNodes) if (c instanceof El) s += c.textContent;
        return s;
    }
    set textContent (v) { this._text = String (v); this.childNodes = []; }

    get innerHTML () { return this._html || ''; }
    set innerHTML (v) { this._html = String (v); this.childNodes = []; }

    get children () { return this.childNodes.filter (n => n instanceof El); }
    get firstChild () { return this.childNodes[0] || null; }

    appendChild (n) {
        if (!n) throw new Error ('appendChild(null)');
        n.parentNode = this;
        this.childNodes.push (n);
        return n;
    }
    append (...ns) { for (const n of ns) this.appendChild (n); }
    removeChild (n) {
        const i = this.childNodes.indexOf (n);
        if (i >= 0) this.childNodes.splice (i, 1);
        return n;
    }

    setAttribute (k, v) {
        this.attributes[k] = String (v);
        if (k === 'id') this.id = v;
        if (k === 'class') this.className = v;
    }
    getAttribute (k) { return this.attributes[k] === undefined ? null : this.attributes[k]; }
    removeAttribute (k) { delete this.attributes[k]; }

    addEventListener (type, fn) { (this.listeners[type] = this.listeners[type] || []).push (fn); }
    removeEventListener (type, fn) {
        const l = this.listeners[type];
        if (!l) return;
        const i = l.indexOf (fn);
        if (i >= 0) l.splice (i, 1);
    }

    // Pointer capture is a no-op here; the knob only needs it not to throw.
    setPointerCapture () {}
    releasePointerCapture () {}
    focus () {}

    // Test helper — synthesise an event.
    fire (type, props) {
        const ev = Object.assign ({
            type, target: this, preventDefault () {}, stopPropagation () {},
            shiftKey: false, clientX: 0, clientY: 0, pointerId: 1, deltaY: 0
        }, props || {});
        for (const fn of (this.listeners[type] || []).slice ()) fn (ev);
        return ev;
    }

    // Test helper — depth-first walk.
    walk (fn) {
        fn (this);
        for (const c of this.children) c.walk (fn);
    }

    querySelectorAll (sel) {
        const out = [];
        const cls = sel.startsWith ('.') ? sel.slice (1) : null;
        const tag = cls ? null : sel.toUpperCase ();
        this.walk (el => {
            if (el === this) return;
            if (cls ? el.classList.contains (cls) : el.tagName === tag) out.push (el);
        });
        return out;
    }
    querySelector (sel) { return this.querySelectorAll (sel)[0] || null; }
}

class SvgEl extends El {
    constructor (doc, tag) { super (doc, tag); this.isSvg = true; }
}

export function createDocument () {
    const doc = {
        _byId: new Map (),
        createElement (tag) { return new El (doc, tag); },
        createElementNS (ns, tag) { return new SvgEl (doc, tag); },
        getElementById (id) { return doc._byId.get (id) || null; },
        querySelectorAll (sel) { return doc.body ? doc.body.querySelectorAll (sel) : []; },
        querySelector (sel) { return doc.querySelectorAll (sel)[0] || null; }
    };
    doc.body = new El (doc, 'body');
    return doc;
}

const VOID_TAGS = new Set (['meta', 'link', 'br', 'img', 'input', 'hr', 'source']);
const SKIP_TAGS = new Set (['script', 'style', 'title', 'head']);

// Tiny parser for the markup subset index.html uses: nested elements with
// double-quoted attributes. Enough to catch an id the JS expects and the HTML
// does not have.
export function parseHTML (html, doc) {
    html = html.replace (/<!--[\s\S]*?-->/g, '').replace (/<!doctype[^>]*>/gi, '');
    const tokens = /<\/([a-zA-Z0-9-]+)\s*>|<([a-zA-Z0-9-]+)((?:\s[^>]*?)?)(\/?)>/g;
    const stack = [doc.body];
    let m;
    while ((m = tokens.exec (html))) {
        if (m[1]) {
            if (SKIP_TAGS.has (m[1].toLowerCase ())) continue;
            if (stack.length > 1) stack.pop ();
            continue;
        }
        const tag = m[2].toLowerCase ();
        if (SKIP_TAGS.has (tag)) continue;
        const el = doc.createElement (tag);
        const attrs = /([a-zA-Z-]+)\s*=\s*"([^"]*)"/g;
        let a;
        while ((a = attrs.exec (m[3] || ''))) el.setAttribute (a[1], a[2]);
        stack[stack.length - 1].appendChild (el);
        if (!VOID_TAGS.has (tag) && !m[4]) stack.push (el);
    }
    return doc.body;
}

// Frame pump — the UI drives an endless requestAnimationFrame loop, so the
// test decides how many frames actually run.
export function installRaf (globalObj) {
    const queue = [];
    globalObj.requestAnimationFrame = (cb) => { queue.push (cb); return queue.length; };
    globalObj.cancelAnimationFrame = () => {};
    return function pump (n) {
        for (let i = 0; i < n; i++) {
            const batch = queue.splice (0, queue.length);
            if (!batch.length) return i;
            for (const cb of batch) cb (i);
        }
        return n;
    };
}
