// Editor de documentos: linhas, escolha de cliente, artigos e câmbio.
// Os totais vêm sempre do servidor (htmx em #totais); aqui só se mostram.
(function () {
    "use strict";

    var form = document.getElementById("formDocumento");
    if (!form) return;
    var tbody = document.getElementById("linhas");
    var modelo = document.getElementById("modeloLinha");
    var urlBase = window.location.pathname.replace(/\/\d+$/, "").replace(/\/$/, "");

    function recalcular() { htmx.trigger(document.body, "recalcular"); }

    // ---------- Linhas ----------
    function renumerar() {
        tbody.querySelectorAll("tr.linha-documento").forEach(function (tr, i) {
            tr.querySelectorAll("[name]").forEach(function (el) {
                el.name = el.name.replace(/Dados\.Linhas\[(\d+|__i__)\]/, "Dados.Linhas[" + i + "]");
            });
        });
    }

    function novaLinha() {
        var tr = modelo.content.firstElementChild.cloneNode(true);
        tbody.appendChild(tr);
        renumerar();
        tr.querySelector(".campo-ref").focus();
    }

    document.getElementById("adicionarLinha").addEventListener("click", novaLinha);

    tbody.addEventListener("click", function (e) {
        var botao = e.target.closest(".remover-linha");
        if (!botao) return;
        botao.closest("tr").remove();
        if (!tbody.querySelector("tr.linha-documento")) novaLinha();
        renumerar();
        recalcular();
    });

    // Enter no último campo de uma linha cria a seguinte (rapidez a facturar muitas linhas).
    tbody.addEventListener("keydown", function (e) {
        if (e.key !== "Enter" || e.target.tagName === "TEXTAREA") return;
        e.preventDefault();
        var tr = e.target.closest("tr");
        if (tr === tbody.lastElementChild) novaLinha();
    });

    // Referência de artigo → preenche descrição, unidade, preço e IVA.
    tbody.addEventListener("change", function (e) {
        if (!e.target.classList.contains("campo-ref")) return;
        var tr = e.target.closest("tr");
        var codigo = e.target.value.trim();
        if (!codigo) { tr.querySelector(".campo-artigo").value = ""; return; }
        fetch(urlBase + "?handler=Artigo&codigo=" + encodeURIComponent(codigo), { headers: { "Accept": "application/json" } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (a) {
                if (!a) { tr.querySelector(".campo-artigo").value = ""; return; }
                tr.querySelector(".campo-artigo").value = a.id;
                tr.querySelector(".campo-ref").value = a.codigo;
                var desc = tr.querySelector(".campo-descricao");
                if (!desc.value.trim()) desc.value = a.descricao;
                tr.querySelector(".campo-unidade").value = a.unidade;
                tr.querySelector(".campo-preco").value = a.preco;
                var taxa = tr.querySelector(".campo-taxa");
                if (taxa.querySelector('option[value="' + a.taxa + '"]')) taxa.value = a.taxa;
                recalcular();
            });
    });

    // Totais por linha vindos do servidor.
    document.body.addEventListener("htmx:afterSwap", function (e) {
        if (e.target.id !== "totais") return;
        var json = document.getElementById("totaisLinhas");
        var dados = json ? JSON.parse(json.textContent) : { linhas: [], total: "—" };
        tbody.querySelectorAll("tr.linha-documento").forEach(function (tr, i) {
            tr.querySelector(".total-linha").textContent = dados.linhas[i] !== undefined ? dados.linhas[i] : "—";
        });
        var rodape = document.getElementById("totalRodape");
        if (rodape) rodape.textContent = dados.total;
    });

    // ---------- Cliente ----------
    var escolhido = document.getElementById("clienteEscolhido");
    var blocoPesquisa = document.getElementById("pesquisaClienteBloco");
    var resultados = document.getElementById("resultadosCliente");

    resultados.addEventListener("click", function (e) {
        var a = e.target.closest(".escolher-cliente");
        if (!a) return;
        e.preventDefault();
        fetch(urlBase + "?handler=Cliente&id=" + a.dataset.id, { headers: { "Accept": "application/json" } })
            .then(function (r) { return r.json(); })
            .then(function (c) {
                document.getElementById("clienteId").value = c.id;
                document.getElementById("clienteNome").textContent = c.nome;
                document.getElementById("clienteNuit").textContent = c.nuit ? "NUIT " + c.nuit : "sem NUIT";
                escolhido.classList.remove("d-none");
                blocoPesquisa.classList.add("d-none");
                resultados.innerHTML = "";

                var venc = document.getElementById("vencimento");
                var data = document.getElementById("dataDocumento");
                if (venc && data.value && c.prazo !== null) {
                    var d = new Date(data.value + "T00:00:00");
                    d.setDate(d.getDate() + c.prazo);
                    venc.value = d.toISOString().slice(0, 10);
                }
                var desconto = document.getElementById("descontoGlobal");
                if (desconto && (!desconto.value || desconto.value === "0,00") && c.desconto !== "0,00") desconto.value = c.desconto;
                recalcular();
            });
    });

    var trocar = document.getElementById("trocarCliente");
    if (trocar) trocar.addEventListener("click", function () {
        escolhido.classList.add("d-none");
        blocoPesquisa.classList.remove("d-none");
        document.getElementById("pesquisaCliente").focus();
    });

    renumerar();
    recalcular();
})();
