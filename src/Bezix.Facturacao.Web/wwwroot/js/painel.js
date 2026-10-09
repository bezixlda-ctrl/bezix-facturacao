// Painel: gráficos carregados depois da página (ApexCharts).
(function () {
    "use strict";
    if (typeof ApexCharts === "undefined") return;

    var raiz = getComputedStyle(document.documentElement);
    var cor = function (nome, alternativa) { return (raiz.getPropertyValue(nome) || alternativa).trim(); };
    var primaria = cor("--bz-primaria", "#0F5C8C");
    var sucesso = cor("--bz-sucesso", "#1F8A5B");
    var escuro = document.documentElement.getAttribute("data-bs-theme") === "dark";
    var texto = cor("--bz-texto-suave", "#5B6676");
    var formato = new Intl.NumberFormat("pt-PT", { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: "always" });
    var mt = function (v) { return formato.format(v).replace(/\s| | /g, ".") + " MT"; };
    var curto = function (v) {
        if (Math.abs(v) >= 1e6) return (v / 1e6).toFixed(1).replace(".", ",") + " M";
        if (Math.abs(v) >= 1e3) return Math.round(v / 1e3) + " mil";
        return Math.round(v).toString();
    };
    var base = {
        chart: { fontFamily: "Manrope, system-ui, sans-serif", foreColor: texto, toolbar: { show: false }, zoom: { enabled: false }, background: "transparent" },
        theme: { mode: escuro ? "dark" : "light" },
        grid: { borderColor: escuro ? "#2A3340" : "#E2E7EE", strokeDashArray: 4 },
        dataLabels: { enabled: false },
        tooltip: { y: { formatter: mt } },
    };
    var principal = null, donut = null;

    function carregar(periodo) {
        fetch("?handler=Graficos&periodo=" + periodo, { headers: { "Accept": "application/json" } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d) desenhar(d); });
    }

    function desenhar(d) {
        var elPrincipal = document.getElementById("graficoPrincipal");
        if (elPrincipal) {
            var series = [];
            if (d.facturado) series.push({ name: "Facturado", data: d.facturado });
            if (d.recebido) series.push({ name: "Recebido", data: d.recebido });
            var opcoes = Object.assign({}, base, {
                chart: Object.assign({}, base.chart, { type: "area", height: 260 }),
                series: series,
                colors: [d.facturado ? primaria : sucesso, sucesso],
                stroke: { curve: "smooth", width: 2.5 },
                fill: { type: "gradient", gradient: { opacityFrom: 0.28, opacityTo: 0.02 } },
                xaxis: { categories: d.rotulos, tickAmount: Math.min(d.rotulos.length, 12), axisBorder: { show: false }, axisTicks: { show: false } },
                yaxis: { labels: { formatter: curto } },
                legend: { show: false },
            });
            if (principal) principal.updateOptions(opcoes); else { principal = new ApexCharts(elPrincipal, opcoes); principal.render(); }
        }

        var elDonut = document.getElementById("graficoAntiguidade");
        if (elDonut && d.antiguidade && !donut && d.antiguidade.some(function (v) { return v > 0; })) {
            donut = new ApexCharts(elDonut, Object.assign({}, base, {
                chart: Object.assign({}, base.chart, { type: "donut", height: 270 }),
                series: d.antiguidade,
                labels: ["0–30 dias", "31–60 dias", "61–90 dias", "Mais de 90 dias"],
                colors: ["#E8B65A", "#E08A3C", "#D2603A", "#B4302A"],
                legend: { position: "bottom" },
                plotOptions: { pie: { donut: { size: "68%", labels: { show: true, total: { show: true, label: "Vencido", formatter: function (w) {
                    return curto(w.globals.seriesTotals.reduce(function (a, b) { return a + b; }, 0)) + " MT";
                } } } } } },
                stroke: { width: 0 },
            }));
            donut.render();
        }

        document.querySelectorAll(".spark").forEach(function (el) {
            if (el.dataset.pronto) return;
            var dados = d[el.dataset.serie];
            if (!dados) return;
            el.dataset.pronto = "1";
            new ApexCharts(el, {
                chart: { type: "area", height: 44, sparkline: { enabled: true }, animations: { enabled: false } },
                series: [{ data: dados }],
                stroke: { curve: "smooth", width: 2 },
                colors: ["#FFFFFF"],
                fill: { type: "solid", opacity: 0.18 },
                tooltip: { enabled: false },
            }).render();
        });
    }

    var botoes = document.getElementById("periodoGrafico");
    if (botoes) botoes.addEventListener("click", function (e) {
        var b = e.target.closest("[data-periodo]");
        if (!b) return;
        botoes.querySelectorAll("button").forEach(function (x) { x.classList.toggle("active", x === b); });
        carregar(b.dataset.periodo);
    });

    carregar("Mensal");
})();
