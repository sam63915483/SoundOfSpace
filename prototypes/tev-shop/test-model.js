// Runs the shop's PURE MODEL (prices, stepper bounds, stack caps, affordability)
// with no DOM, the same trick the shuttle-computer prototype uses on its engine.
// The model is the half that can be wrong in a way you would not see by looking.
//
//   node prototypes/tev-shop/test-model.js

const fs = require('fs');
const path = require('path');

const src = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
const js = src.split('<script>')[1].split('</script>')[0];
const model = js.split('// ── row builders')[0];

// new Function rather than eval: a bare eval leaks its `function` declarations
// into this module's scope, where they collide with the names below.
const M = new Function(model +
  ';return {canAdd,buyTapes,buyPlugin,newState,lineCost,MODULES,BLANKS,STACK_MAX};')();
const { canAdd, buyTapes, buyPlugin, newState, lineCost, MODULES, BLANKS, STACK_MAX } = M;

let checks = 0, failed = 0;
const ck = (cond, what) => {
  checks++;
  if (!cond) { failed++; console.log('  FAIL  ' + what); }
};
const T1 = BLANKS[0], T2 = BLANKS[1];

// ── the purse ────────────────────────────────────────────────────────────
let s = newState();
ck(s.money === 240, 'starts with $240');
ck(lineCost(s, T1) === 0, 'an untouched row costs nothing');

// ── the stepper is bounded by BOTH the stack cap and the purse ───────────
s = newState();
s.qty.T1 = STACK_MAX;
ck(!canAdd(s, T1), 'the stepper stops at the ' + STACK_MAX + ' stack cap');
ck(s.money > lineCost(s, T1), 'even with money to spare');

s = newState();
s.money = 12;                       // $12 buys two Type 1 at $5, not three
s.qty.T1 = 2;
ck(!canAdd(s, T1), 'and stops early when the money runs out first');
ck(lineCost(s, T1) === 10, 'leaving a line you can actually pay');

// ── buying tapes ─────────────────────────────────────────────────────────
s = newState();
s.qty.T1 = 4;
ck(buyTapes(s, 'T1'), 'buying a filled row succeeds');
ck(s.money === 220, '4 x $5 came out of the purse');
ck(s.qty.T1 === 0, 'and the row resets to zero');

ck(!buyTapes(s, 'T1'), 'buying an empty row does nothing');
ck(s.money === 220, 'and costs nothing');

// Type 2 is three times Type 1 - the thing the rebalance leans on.
s = newState();
s.qty.T2 = 1;
buyTapes(s, 'T2');
ck(s.money === 225, 'a Type 2 blank is $15');

// ── buying plugins ───────────────────────────────────────────────────────
s = newState();
ck(buyPlugin(s, 'SIREN'), 'SIREN is for sale');
ck(s.money === 180 && s.owned.has('SIREN'), 'it costs $60 and installs');
ck(!buyPlugin(s, 'SIREN'), 'and cannot be bought twice');
ck(s.money === 180, 'a refused second purchase costs nothing');

ck(!buyPlugin(s, 'THUMPER'), 'the modules you land with are not for sale');
ck(!buyPlugin(s, 'GLOWORM'), 'neither of them');
ck(s.money === 180, 'and neither charged you');

// CAVE is $180 - affordable at exactly the purse, not a credit less.
s = newState();
s.money = 180;
ck(buyPlugin(s, 'CAVE'), 'a plugin costing exactly your balance is affordable');
ck(s.money === 0, 'and empties the purse');
s = newState();
s.money = 179;
ck(!buyPlugin(s, 'CAVE'), 'one credit short is refused');

// ── no cart means no cross-row overspend ─────────────────────────────────
// The v1 mockup had a basket, where tapes and plugins were added in different
// places and each half could look affordable while the total was not. Buying
// per row removes that failure entirely - this pins it shut.
s = newState();
s.qty.T1 = 20;                      // $100 pending, NOT yet paid
ck(buyPlugin(s, 'CAVE'), 'a pending tape row does not block a plugin...');
ck(s.money === 60, '...because nothing is owed until the row is bought');
ck(!canAdd(s, T1), 'and the stepper immediately re-bounds to the smaller purse');
ck(!buyTapes(s, 'T1'), 'a row that is now unaffordable refuses rather than overdrawing');
ck(s.money === 60, 'and the purse never goes negative');

// ── the catalogue, as shipped ────────────────────────────────────────────
ck(MODULES.filter(m => !m.start).reduce((a, m) => a + m.price, 0) === 460,
   'the four plugins total $460');
ck(MODULES.length === 6, 'the rack is a set of six');
ck(T1.price === 5 && T2.price === 15, 'blanks are $5 and $15');

console.log(failed
  ? '\nshop model FAILED - ' + failed + ' of ' + checks + ' checks'
  : '\nshop model OK - ' + checks + ' checks, all passed');
process.exit(failed ? 1 : 0);
