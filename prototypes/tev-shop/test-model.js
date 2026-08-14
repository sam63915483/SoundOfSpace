// Runs the shop's PURE MODEL (prices, basket, stack caps, affordability) with
// no DOM, the same trick the shuttle-computer prototype uses on its engine.
// The model is the half that can be wrong in a way you would not see by looking.
//
//   node prototypes/tev-shop/test-model.js

const fs = require('fs');
const path = require('path');

const src = fs.readFileSync(path.join(__dirname, 'index.html'), 'utf8');
const js = src.split('<script>')[1].split('</script>')[0];
const model = js.split('// ── A — THE COUNTER')[0];

// new Function rather than eval: a bare eval leaks its `function` declarations
// into this module's scope, where they collide with the names below.
const M = new Function(
  model + ';return {cartTotal,canAdd,pick,pay,newState,MODULES,BLANKS,STACK_MAX};')();
const { cartTotal, canAdd, pick, pay, newState, MODULES, BLANKS, STACK_MAX } = M;

let checks = 0, failed = 0;
const ck = (cond, what) => {
  checks++;
  if (!cond) { failed++; console.log('  FAIL  ' + what); }
};
const mod = id => MODULES.find(m => m.id === id);

let s = newState();
ck(s.money === 240, 'starts with $240');
ck(cartTotal(s) === 0, 'an empty basket costs nothing');

s.cart.T1 = 4;
ck(cartTotal(s) === 20, '4 x T1 is $20');
s.cart.T2 = 2;
ck(cartTotal(s) === 50, 'plus 2 x T2 is $50');

// A module joins the SAME basket, so one PAY settles both kinds.
pick(s, mod('SIREN'));
ck(cartTotal(s) === 110, 'SIREN adds $60 to the same basket');
pick(s, mod('SIREN'));
ck(cartTotal(s) === 50, 'clicking it again takes it back out');

// THE BUG A SPLIT UI INVITES: tapes and modules are added in different places,
// so each half can look affordable while the total is not.
s = newState();
s.cart.T2 = 12;                       // $180 of tape
pick(s, mod('CAVE'));                 // + $180 = $360, over the $240 purse
ck(!s.picked.has('CAVE'), 'a module you cannot afford ALONGSIDE the tapes is refused');
ck(cartTotal(s) === 180, 'and the basket is unchanged by the refusal');

s = newState();
pick(s, mod('CAVE'));                 // $180 of the $240
ck(s.picked.has('CAVE'), 'the same module IS affordable on its own');
s.cart.T1 = 12;                       // $60 -> exactly $240
ck(cartTotal(s) === 240, 'a basket can be filled to the last credit');
ck(!canAdd(s, BLANKS[0]), 'and one more tape is refused for money, not stack room');

// Stack cap, independent of money.
s = newState();
s.cart.T1 = STACK_MAX;
ck(!canAdd(s, BLANKS[0]), 'blanks stop at the ' + STACK_MAX + ' stack cap');
ck(s.money > cartTotal(s), 'even with money to spare');

// Paying.
s = newState();
s.cart.T1 = 4;
pick(s, mod('SIREN'));
const owed = cartTotal(s);
pay(s);
ck(s.money === 240 - owed, 'paying debits exactly the basket total');
ck(s.owned.has('SIREN'), 'and installs the module');
ck(cartTotal(s) === 0 && s.cart.T1 === 0, 'and empties the basket');

pick(s, mod('SIREN'));
ck(cartTotal(s) === 0, 'an installed module cannot be bought a second time');

// The two you land with are not stock.
s = newState();
pick(s, mod('THUMPER'));
pick(s, mod('GLOWORM'));
ck(cartTotal(s) === 0, 'the two modules you start with are not for sale');

// Paying for nothing must not be possible.
s = newState();
const before = s.money;
pay(s);
ck(s.money === before, 'paying an empty basket costs nothing');

// The ladder, as shipped.
ck(MODULES.filter(m => !m.start).reduce((a, m) => a + m.price, 0) === 460,
   'the four plugins total $460');
ck(MODULES.length === 6, 'the rack is a set of six');

console.log(failed
  ? '\nshop model FAILED - ' + failed + ' of ' + checks + ' checks'
  : '\nshop model OK - ' + checks + ' checks, all passed');
process.exit(failed ? 1 : 0);
