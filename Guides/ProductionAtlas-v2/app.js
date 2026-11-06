(function () {
  'use strict';
  const E = ProductionEngine.create(ATLAS_DATA);
  const $ = id => document.getElementById(id);
  const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const num = n => new Intl.NumberFormat('en', { maximumFractionDigits: 3 }).format(Math.abs(n) < 1e-9 ? 0 : n);
  const state = { root: 'VORB', cargo: 'AnyIndustrialProducts', basis: 'surplus', path: [], destination: null, recipe: null, search: '', region: '', includeSinks: false };
  state.shipmentMode = 'pods'; state.shipmentAmount = 1; state.unitMass = E.mass(state.cargo);
  function initialUnits() { return E.shipment(state.shipmentMode, state.shipmentAmount, state.unitMass); }
  function syncMass() { state.unitMass = E.mass(state.cargo); $('unit-mass').value = state.unitMass || ''; }
  function loadText(category, units) {
    const load = E.load(category, units);
    return load.kg === null ? 'Mass estimate unavailable' : num(load.kg) + ' kg · ' + (load.pods === null ? 'unit exceeds pod capacity' : num(load.pods) + ' pods for output');
  }
  let visibleRoutes = [], selectedRoute = null, selectedOutcome = null;
  const option = (value, text) => '<option value="' + esc(value) + '">' + esc(text) + '</option>';
  function cursor() {
    if (state.path.length) {
      const step = state.path[state.path.length - 1];
      return { station: E.stations.get(step.destination), category: step.output, rate: step.outputRate };
    }
    const supply = E.outputs(state.root, state.basis).find(x => x.category === state.cargo);
    return { station: E.stations.get(state.root), category: state.cargo, rate: supply ? (initialUnits() || 0) : 0 };
  }
  function reset() { state.path = []; state.destination = null; state.recipe = null; }
  function setRoot(id, cargo) {
    if (!E.stations.has(id)) return;
    state.root = id;
    state.cargo = cargo || state.cargo;
    reset(); syncCargo(); syncMass(); render();
  }
  function syncCargo() {
    const rows = E.outputs(state.root, state.basis);
    if (!rows.some(r => r.category === state.cargo)) state.cargo = rows.length ? rows[0].category : null;
    $('cargo-select').innerHTML = rows.length ? rows.map(r => option(r.category, E.name(r.category) + ' · ' + num(r.rate) + '/h')).join('') : option('', 'No net production surplus');
    $('cargo-select').value = state.cargo || '';
    $('cargo-select').disabled = !rows.length;
    $('station-select').value = state.root;
  }
  function matches(route) {
    const query = state.search.trim().toLowerCase();
    return !query || [route.station.id, route.station.name, route.station.region, ...route.products.map(E.name), ...route.outcomes.map(o => o.recipe.description)].join(' ').toLowerCase().includes(query);
  }
  function render() {
    const c = cursor();
    const allRoutes = E.routes(c.station.id, c.category, c.rate, { includeSinks: state.includeSinks, region: state.region });
    visibleRoutes = allRoutes.filter(matches);
    selectedRoute = visibleRoutes.find(r => r.station.id === state.destination) || visibleRoutes[0] || null;
    state.destination = selectedRoute ? selectedRoute.station.id : null;
    selectedOutcome = selectedRoute ? selectedRoute.outcomes.find(o => o.recipe.id === state.recipe) || selectedRoute.outcomes[0] : null;
    state.recipe = selectedOutcome ? selectedOutcome.recipe.id : null;
    renderSupply(c); renderBreadcrumbs(); renderDestinations(); renderFlow(c); renderDetail(c);
    $('announcement').textContent = visibleRoutes.length + ' destination stations for ' + E.name(c.category || 'this cargo') + '. ' + (selectedRoute ? selectedRoute.station.name + ' selected.' : 'No matching destinations.');
  }
  function renderSupply(c) {
    const units = initialUnits();
    $('amount-label').textContent = state.shipmentMode === 'pods' ? 'PODS / 1,000 KG EACH' : 'TOTAL CATEGORY UNITS';
    $('shipment-amount').setAttribute('aria-invalid', units === null ? 'true' : 'false');
    $('unit-mass').setAttribute('aria-invalid', state.unitMass > 0 ? 'false' : 'true');
    const category = ATLAS_DATA.categories.find(x => x.id === state.cargo);
    const podTypes = category && category.podTypes ? category.podTypes.filter(Boolean) : [];
    const specs = podTypes.length ? ' · Required pod specs: ' + podTypes.join(' + ') : '';
    $('shipment-summary').textContent = units === null ? 'Enter a whole count from 1 to 1,000,000 and a positive mass for pod mode.' : num(units) + ' initial units · ' + (state.unitMass > 0 ? num(units * state.unitMass) + ' kg' : 'mass unknown') + (state.shipmentMode === 'pods' ? ' / ' + num(state.shipmentAmount * 1000) + ' kg capacity' : '') + specs;
    const row = E.outputs(state.root, 'gross').find(r => r.category === state.cargo);
    const sourceRecipes = E.stations.get(state.root).recipes.map(id => E.recipes.get(id)).filter(r => r.output && r.output.category === state.cargo);
    $('departure-recipes').hidden = !row;
    $('departure-recipes').querySelector('summary').textContent = 'How ' + state.root + ' produces ' + E.name(state.cargo || 'this cargo');
    $('departure-recipe-content').innerHTML = sourceRecipes.map(r => '<p><strong>' + (r.inputs.length ? r.inputs.map(i => num(i.amount * r.cyclesPerHour) + '/h ' + esc(E.name(i.category))).join(' + ') : 'No market inputs') + ' → ' + num(r.cyclesPerHour * r.output.amount) + '/h ' + esc(E.name(r.output.category)) + '</strong><br><code>' + esc(r.id) + '</code></p>').join('') + '<p>Full nominal rates, shared input stocks, and free output storage assumed. These starting-port requirements are not automatically fulfilled by the chain.</p>';
    if (!row) {
      $('supply-line').innerHTML = 'This port has no net surplus in the selected basis. Switch to <strong>All output</strong> to inspect gross production.';
      return;
    }
    const content = '<strong>' + num(row.gross) + '/h gross output</strong><span class="dot">/</span>' + num(row.consumed) + '/h local use<span class="dot">/</span>' + num(Math.max(0, row.net)) + '/h potential surplus';
    $('supply-line').innerHTML = content + '<br>Shipment: <strong>' + num(c.rate) + ' units ' + esc(E.name(c.category || '')) + '</strong> from ' + esc(c.station.id) + '. Supply rates above are context; your shipment is accumulated stock, not an hourly delivery.';
  }
  function renderBreadcrumbs() {
    const root = '<button class="crumb ' + (!state.path.length ? 'current' : '') + '" data-step="0" type="button"' + (!state.path.length ? ' aria-current="step"' : '') + '>' + esc(state.root) + '<small>' + esc(E.name(state.cargo || 'No cargo')) + '</small></button>';
    $('breadcrumbs').innerHTML = root + state.path.map((step, i) => '<span class="crumb-separator" aria-hidden="true">→</span><button class="crumb ' + (i === state.path.length - 1 ? 'current' : '') + '" data-step="' + (i + 1) + '" type="button"' + (i === state.path.length - 1 ? ' aria-current="step"' : '') + '>' + esc(step.destination) + '<small>' + esc(E.name(step.output)) + '</small></button>').join('');
  }
  function renderDestinations() {
    $('destination-count').textContent = String(visibleRoutes.length).padStart(2, '0');
    $('destination-list').innerHTML = visibleRoutes.length ? visibleRoutes.map(route => {
      const labels = route.products.slice(0, 3).map(id => '<span>' + esc(E.name(id)) + '</span>').join('') + (route.products.length > 3 ? '<span>+' + (route.products.length - 3) + ' more</span>' : '') || '<span>Consumption only</span>';
      return '<button type="button" class="destination-card ' + (route.station.id === state.destination ? 'selected' : '') + '" data-station="' + esc(route.station.id) + '" aria-pressed="' + (route.station.id === state.destination) + '"><span class="card-top"><span class="station-code">' + esc(route.station.id) + '</span><span class="region-label">' + esc(route.station.region) + '</span><span class="arrow" aria-hidden="true">↗</span></span><span class="destination-name">' + esc(route.station.name) + '</span><span class="product-chips">' + labels + '</span><span class="card-bottom"><span>' + route.outcomes.length + ' recipe' + (route.outcomes.length === 1 ? '' : 's') + '</span><strong>up to ' + num(route.maxUsed) + ' units input per recipe</strong></span></button>';
    }).join('') : '<div class="empty"><strong>No matching production links.</strong>' + (state.search || state.region ? 'Try clearing the search or choosing all regions.' : 'No other station converts this cargo in the snapshot. You can include consumption-only destinations, choose a different cargo, or go back one step.') + '<button class="empty-action" id="clear-filters" type="button">' + (state.search || state.region ? 'Clear filters' : 'Include consumption') + '</button></div>';
  }
  function connector() { return '<svg class="connector" viewBox="0 0 65 24" aria-hidden="true"><path d="M0 12 H60 M54 7 L60 12 L54 17"/></svg>'; }
  function renderFlow(c) {
    if (!selectedRoute) {
      $('flow-heading').textContent = 'The next link is yours to choose.';
      $('flow').innerHTML = '<div class="empty"><strong>' + (c.category ? esc(E.name(c.category)) + ' · ' + num(c.rate) + ' units' : 'No surplus cargo') + '</strong>' + (state.search || state.region ? 'The current filters hide all destinations. Clear them to see the available links.' : 'Choose another cargo or include consumption-only destinations. Not every produced commodity feeds another production recipe.') + '</div>';
      return;
    }
    $('flow-heading').textContent = c.station.id + ' → ' + selectedRoute.station.id + ' / ' + E.name(c.category);
    const outcomes = selectedRoute.outcomes.map(o => {
      const product = o.recipe.output ? E.name(o.recipe.output.category) : 'Consumption only';
      const nextCount = o.recipe.output ? E.routes(selectedRoute.station.id, o.recipe.output.category, o.outputRate).length : 0;
      const sameProduct = selectedRoute.outcomes.filter(other => other.recipe.output && o.recipe.output && other.recipe.output.category === o.recipe.output.category);
      const recipeLabel = sameProduct.length > 1 ? '<span class="coinput-note">' + (o.recipe.id === 'OKLGIndustrialProductsToHVACConverter' ? 'Recipe named HVAC · game data outputs Control Systems' : 'Recipe ' + (sameProduct.indexOf(o) + 1) + ' of ' + sameProduct.length + ' · ' + esc(o.recipe.description)) + '</span>' : '';
      const co = o.coInputs.length ? 'Also needs ' + o.coInputs.map(i => E.name(i.category)).join(' + ') : 'No additional recipe inputs';
      return '<button type="button" class="output-node ' + (o.recipe.id === state.recipe ? 'selected ' : '') + (!o.recipe.output ? 'sink' : '') + '" data-recipe="' + esc(o.recipe.id) + '" aria-pressed="' + (o.recipe.id === state.recipe) + '"><span class="output-top"><strong>' + esc(product) + '</strong><span class="rate">' + (o.recipe.output ? num(o.outputRate) + ' units' : num(o.used) + ' units used') + '</span></span>' + recipeLabel + '<span class="coinput-note">' + esc(co) + '</span>' + (o.recipe.output ? '<span class="coinput-note">' + esc(loadText(o.recipe.output.category, o.outputRate)) + '</span>' : '') + (o.recipe.output ? '<span class="branch-note">' + nextCount + ' onward production destination' + (nextCount === 1 ? '' : 's') + ' ↗</span>' : '') + '</button>';
    }).join('');
    $('flow').innerHTML = '<div class="flow-inner"><span class="cargo-ribbon">ONE CARGO / ALTERNATIVE OUTCOMES</span><div class="flow-node origin"><span class="node-kicker">' + (state.path.length ? 'CHAIN CONTINUES' : 'DEPARTURE') + '</span><h3>' + esc(c.station.id) + '</h3><p>' + esc(c.station.name) + '</p><strong class="node-rate">' + esc(E.name(c.category)) + '<br>' + num(c.rate) + ' units</strong></div>' + connector() + '<div class="flow-node receiver"><span class="node-kicker">SUPPLY THIS PORT</span><h3>' + esc(selectedRoute.station.id) + '</h3><p>' + esc(selectedRoute.station.name) + '</p><p class="hub-note">' + (selectedRoute.station.hub ? 'Market hub: ' + esc(selectedRoute.station.hub) + ' · transport still required' : esc(selectedRoute.station.region)) + '</p></div>' + connector() + '<div class="output-stack" aria-label="Alternative recipe outcomes">' + outcomes + '</div></div>';
  }
  function renderDetail(c) {
    if (!selectedOutcome) { $('recipe-detail').innerHTML = ''; return; }
    const o = selectedOutcome, r = o.recipe;
    const repeated = state.path.some(step => step.destination === state.destination && step.recipe === r.id);
    const limit = state.path.length >= 6;
    const ingredients = r.inputs.map(i => num(i.amount) + ' ' + esc(E.name(i.category))).join(' + ');
    const output = r.output ? num(r.output.amount) + ' ' + esc(E.name(r.output.category)) : 'consumed';
    const coInputs = o.coInputs.map(i => {
      const balance = E.totals.get(state.destination).get(i.category) || { output: 0 };
      return '<div class="coinput-row"><span>' + esc(E.name(i.category)) + '<small>Port gross production: ' + num(balance.output) + '/h; availability is not assured</small></span><strong>' + num(i.rate) + ' units required</strong></div>';
    }).join('');
    const mismatch = r.id === 'OKLGIndustrialProductsToHVACConverter' ? '<p class="warning-note">Data quirk: this recipe is named “ToHVAC”, but its actual configured output is Control Systems. This atlas follows the output field.</p>' : '';
    $('recipe-detail').innerHTML = '<div class="detail-top"><div><p class="eyebrow">SELECTED RECIPE</p><h3>' + esc(r.output ? E.name(r.output.category) : 'Consumption only') + '</h3><p class="detail-subtitle">At ' + esc(selectedRoute.station.name) + ' · ' + (r.output ? 'conditional output enabled by this delivery' : 'this recipe uses the cargo without a market output') + '</p></div>' + (r.output ? '<button class="primary-button" id="continue-button" type="button"' + (repeated || limit || !o.cycles ? ' disabled' : '') + '>Continue with this output <span aria-hidden="true">→</span></button>' : '') + '</div><div class="recipe-equation">' + ingredients + '<span>→</span>' + output + '<br><span>' + num(r.cyclesPerHour) + ' cycles/game hour · ' + num(60 / r.cyclesPerHour) + ' minutes/cycle</span></div><dl class="stat-grid"><div><dt>Your cargo used</dt><dd>' + num(o.used) + '<small> units</small></dd></div><div><dt>Unallocated cargo</dt><dd>' + num(o.remaining) + '<small> units</small></dd></div><div><dt>' + (r.output ? 'Conditional output' : 'Consumed') + '</dt><dd>' + num(r.output ? o.outputRate : o.used) + '<small>' + ' units' + '</small></dd></div></dl><div class="requirements"><h4>' + (o.coInputs.length ? 'Co-inputs for the full run' : 'No co-inputs required by this recipe') + '</h4>' + (coInputs || '<p>Your selected cargo is the only ingredient. Production still needs free output storage and actual delivered stock.</p>') + (o.coInputs.length ? '<p>Every co-input must also be supplied. Co-input quantities are totals for this run. The output shown is a ceiling, not production achievable from this cargo alone. Local recipes may compete for those goods.</p>' : '') + '</div>' + (repeated ? '<p class="warning-note">This transformation already appears in your chain. Repeating it would create a cycle; go back or choose another output.</p>' : '') + (limit ? '<p class="warning-note">Six transformations are shown. Go back along the breadcrumbs to explore another branch.</p>' : '') + mismatch + '<div class="shipment-result"><strong>' + num(o.cycles) + ' complete cycles · ' + num(o.hours) + ' game hours processing</strong><p>' + (r.output ? esc(loadText(r.output.category, o.outputRate)) : 'No onward output') + '</p><p>Chain processing so far, including this run: ' + num(state.path.reduce((sum, step) => sum + step.hours, 0) + o.hours) + ' game hours. Excludes travel, waiting, and scheduling delays.</p>' + (!o.cycles ? '<p>Not enough input for one whole cycle. Increase the initial shipment.</p>' : '') + (r.output && state.shipmentMode === 'pods' && E.load(r.output.category, o.outputRate).pods > state.shipmentAmount ? '<p class="warning-note">Output exceeds your initial pod count. Carrying it all needs more pods or multiple trips.</p>' : '') + '</div>' + '<div class="recipe-source">RECIPE / ' + esc(r.id) + '</div>';
  }
  function advance() {
    if (!selectedOutcome || !selectedOutcome.recipe.output || selectedOutcome.outputRate <= 0 || state.path.length >= 6 || state.path.some(s => s.destination === state.destination && s.recipe === selectedOutcome.recipe.id)) return;
    const c = cursor(), o = selectedOutcome;
    state.path.push({ origin: c.station.id, incoming: c.category, incomingRate: c.rate, destination: state.destination, recipe: o.recipe.id, used: o.used, remaining: o.remaining, output: o.recipe.output.category, outputRate: o.outputRate, coInputs: o.coInputs, hours: o.hours });
    state.destination = null; state.recipe = null; state.search = ''; $('destination-search').value = '';
    render(); $('breadcrumbs').scrollLeft = $('breadcrumbs').scrollWidth;
    $('flow-heading').scrollIntoView({ block: 'nearest', behavior: 'auto' });
  }
  function exportNotes() {
    let text = 'OSTRANAUTS — PRODUCTION ATLAS V2\nPotential shipment chain, snapshot ' + ATLAS_DATA.meta.referenceDate + '\n\n';
    text += 'Departure: ' + state.root + ' — ' + E.stations.get(state.root).name + '\nInitial cargo: ' + E.name(state.cargo || 'none') + ' · ' + num(initialUnits() || 0) + ' units\nEntered: ' + state.shipmentAmount + ' ' + state.shipmentMode + '; initial mass: ' + state.unitMass + ' kg/unit\n';
    const steps = state.path.slice();
    if (selectedOutcome) {
      const c = cursor(), o = selectedOutcome;
      steps.push({ origin: c.station.id, incoming: c.category, incomingRate: c.rate, destination: state.destination, recipe: o.recipe.id, used: o.used, remaining: o.remaining, output: o.recipe.output && o.recipe.output.category, outputRate: o.outputRate, coInputs: o.coInputs, hours: o.hours, preview: true });
    }
    steps.forEach((s, i) => {
      text += '\n' + (i + 1) + '. ' + s.origin + ' → ' + s.destination + (s.preview ? ' [selected next leg]' : '') + '\n  Input: ' + E.name(s.incoming) + ', ' + num(s.used) + ' units used of ' + num(s.incomingRate) + '; ' + num(s.remaining) + ' units left at receiving port\n  Output: ' + (s.output ? E.name(s.output) + ', ' + num(s.outputRate) + ' units; ' + loadText(s.output, s.outputRate) : 'consumption only') + '\n  Processing: ' + num(s.hours) + ' game hours\n  Recipe: ' + s.recipe + '\n';
      s.coInputs.forEach(input => { text += '  Also requires: ' + E.name(input.category) + ', ' + num(input.rate) + ' units total\n'; });
    });
    text += '\nAll steps use whole cycles and assume separately supplied co-inputs and storage cleared as needed. Full output carries onward; leftovers do not. Pod estimates cover output only; additional pods or trips may be needed. Masses are category estimates. Branches are alternatives, not additive. No live stock, prices, travel times, or profit forecasts.\n';
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
    const a = document.createElement('a'); a.href = url; a.download = 'production-chain-' + state.root + '.txt'; a.hidden = true; document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  $('station-select').innerHTML = ATLAS_DATA.stations.map(s => option(s.id, s.id + ' — ' + s.name)).join('');
  $('region-select').innerHTML += [...new Set(ATLAS_DATA.stations.map(s => s.region))].sort().map(r => option(r, r)).join('');
  $('snapshot-date').textContent = ATLAS_DATA.meta.referenceDate;
  $('station-select').addEventListener('change', e => setRoot(e.target.value));
  $('cargo-select').addEventListener('change', e => { state.cargo = e.target.value; syncMass(); reset(); render(); });
  document.querySelectorAll('input[name="basis"]').forEach(input => input.addEventListener('change', e => { state.basis = e.target.value; reset(); syncCargo(); syncMass(); render(); }));
  $('reset-button').addEventListener('click', () => { reset(); state.search = ''; state.region = ''; $('destination-search').value = ''; $('region-select').value = ''; render(); });
  $('destination-search').addEventListener('input', e => { state.search = e.target.value; render(); });
  $('region-select').addEventListener('change', e => { state.region = e.target.value; state.destination = null; render(); });
  $('include-sinks').addEventListener('change', e => { state.includeSinks = e.target.checked; render(); });
  $('destination-list').addEventListener('click', e => {
    const button = e.target.closest('[data-station]');
    if (button) { state.destination = button.dataset.station; state.recipe = null; render(); const replacement = $('destination-list').querySelector('[data-station="' + state.destination + '"]'); if (replacement) replacement.focus({ preventScroll: true }); }
    if (e.target.closest('#clear-filters')) {
      if (!state.search && !state.region) { state.includeSinks = true; $('include-sinks').checked = true; }
      state.search = ''; state.region = ''; $('destination-search').value = ''; $('region-select').value = ''; render();
    }
  });
  $('flow').addEventListener('click', e => {
    const button = e.target.closest('[data-recipe]');
    if (!button || !selectedRoute) return;
    state.recipe = button.dataset.recipe;
    selectedOutcome = selectedRoute.outcomes.find(o => o.recipe.id === state.recipe);
    renderFlow(cursor()); renderDetail(cursor());
    const replacement = $('flow').querySelector('[data-recipe="' + state.recipe + '"]'); if (replacement) replacement.focus({ preventScroll: true });
    $('announcement').textContent = (selectedOutcome.recipe.output ? E.name(selectedOutcome.recipe.output.category) : 'Consumption') + ' recipe selected.';
  });
  $('recipe-detail').addEventListener('click', e => { if (e.target.closest('#continue-button')) advance(); });
  $('breadcrumbs').addEventListener('click', e => { const button = e.target.closest('[data-step]'); if (button) { state.path = state.path.slice(0, Number(button.dataset.step)); state.destination = null; state.recipe = null; render(); } });
  $('export-button').addEventListener('click', exportNotes);
  $('about-button').addEventListener('click', () => $('about-dialog').showModal());
  $('close-about').addEventListener('click', () => $('about-dialog').close());
  $('about-dialog').addEventListener('click', e => { if (e.target === $('about-dialog')) { const r = e.target.getBoundingClientRect(); if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) e.target.close(); } });
  document.querySelectorAll('input[name="shipment-mode"]').forEach(input => input.addEventListener('change', e => { state.shipmentMode = e.target.value; reset(); render(); }));
  $('shipment-amount').addEventListener('input', e => { state.shipmentAmount = e.target.value === '' ? NaN : Number(e.target.value); reset(); render(); });
  $('unit-mass').addEventListener('input', e => { state.unitMass = e.target.value === '' ? NaN : Number(e.target.value); reset(); render(); });
  syncCargo(); syncMass(); render();
  window.AtlasApp = { engine: E, state, cursor, setRoot, render, reset, advance };
})();
