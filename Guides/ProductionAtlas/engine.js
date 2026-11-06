/* Pure production-chain calculations. No DOM, network, or runtime game state. */
(function (global) {
  'use strict';
  function create(data) {
    const recipes = new Map(data.recipes.map(r => [r.id, r]));
    const stations = new Map(data.stations.map(s => [s.id, s]));
    const names = new Map(data.categories.map(c => [c.id, c.name]));
    const totals = new Map();
    data.stations.forEach(station => {
      const balance = new Map();
      function add(category, key, amount) {
        if (!balance.has(category)) balance.set(category, { input: 0, output: 0 });
        balance.get(category)[key] += amount;
      }
      station.recipes.forEach(id => {
        const r = recipes.get(id);
        if (!r) throw new Error('Missing recipe: ' + id);
        r.inputs.forEach(i => add(i.category, 'input', i.amount * r.cyclesPerHour));
        if (r.output) add(r.output.category, 'output', r.output.amount * r.cyclesPerHour);
      });
      totals.set(station.id, balance);
    });
    function name(category) { return names.get(category) || category; }
    function outputs(stationId, mode) {
      const rows = [];
      (totals.get(stationId) || new Map()).forEach((v, category) => {
        const net = v.output - v.input;
        const rate = mode === 'surplus' ? Math.max(0, net) : v.output;
        if (rate > 1e-9) rows.push({ category, rate, gross: v.output, consumed: v.input, net });
      });
      return rows.sort((a, b) => name(a.category).localeCompare(name(b.category)));
    }
    function outcome(recipe, category, incomingRate) {
      const ingredient = recipe.inputs.find(i => i.category === category);
      if (!ingredient || ingredient.amount <= 0 || incomingRate <= 0) return null;
      const cycles = Math.min(recipe.cyclesPerHour, incomingRate / ingredient.amount);
      return {
        recipe, cycles,
        used: cycles * ingredient.amount,
        remaining: Math.max(0, incomingRate - cycles * ingredient.amount),
        outputRate: recipe.output ? cycles * recipe.output.amount : 0,
        utilization: recipe.cyclesPerHour ? cycles / recipe.cyclesPerHour : 0,
        coInputs: recipe.inputs.filter(i => i.category !== category).map(i => ({ category: i.category, amount: i.amount, rate: cycles * i.amount }))
      };
    }
    function routes(stationId, category, incomingRate, options) {
      options = options || {};
      const rows = [];
      data.stations.forEach(station => {
        if (station.id === stationId || (options.region && station.region !== options.region)) return;
        const outcomes = station.recipes.map(id => outcome(recipes.get(id), category, incomingRate))
          .filter(o => o && (options.includeSinks || o.recipe.output));
        if (!outcomes.length) return;
        const products = [...new Set(outcomes.filter(o => o.recipe.output).map(o => o.recipe.output.category))];
        rows.push({ station, outcomes, products, maxUsed: Math.max(...outcomes.map(o => o.used)), productionCount: outcomes.filter(o => o.recipe.output).length });
      });
      return rows.sort((a, b) => b.products.length - a.products.length || b.maxUsed - a.maxUsed || a.station.id.localeCompare(b.station.id));
    }
    return { data, recipes, stations, totals, name, outputs, outcome, routes };
  }
  global.ProductionEngine = { create };
})(window);
