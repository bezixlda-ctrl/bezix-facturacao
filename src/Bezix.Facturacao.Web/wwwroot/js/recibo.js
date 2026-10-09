// Registo de recibo: escolha de cliente, repartição do valor pelas facturas e resumo.
(function () {
    "use strict";

    var form = document.getElementById("formRecibo");
    if (!form) return;

    function ler(texto) {
        // "1.234,56" → 1234.56 ; "1234.56" → 1234.56
        texto = (texto || "").trim().replace(/\s|MT/gi, "");
        if (!texto) return 0;
        if (texto.indexOf(",") >= 0) texto = texto.replace(/\./g, "").replace(",", ".");
        var n = parseFloat(texto);
        return isNaN(n) ? 0 : Math.round(n * 100) / 100;
    }
    var formato = new Intl.NumberFormat("pt-PT", { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: "always" });
    function escrever(n) { return formato.format(n).replace(/\s/g, ".").replace(/ | /g, "."); }

    function campos() { return Array.prototype.slice.call(form.querySelectorAll(".valor-alocar")); }

    function resumo() {
        var recebido = ler(document.getElementById("valorRecebido").value);
        var alocado = campos().reduce(function (s, c) { return s + ler(c.value); }, 0);
        alocado = Math.round(alocado * 100) / 100;
        var r = document.getElementById("resumoRecebido");
        if (!r) return;
        r.textContent = escrever(recebido) + " MT";
        document.getElementById("resumoAlocado").textContent = escrever(alocado) + " MT";
        document.getElementById("resumoAdiantamento").textContent = escrever(Math.max(0, recebido - alocado)) + " MT";
        var excesso = alocado > recebido;
        document.getElementById("avisoExcesso").classList.toggle("d-none", !excesso);
        document.getElementById("emitirRecibo").disabled = excesso || recebido <= 0;
    }

    // Reparte o valor recebido pelas facturas, da mais antiga para a mais recente.
    function distribuir() {
        var resto = ler(document.getElementById("valorRecebido").value);
        campos().forEach(function (c) {
            var aberto = parseFloat(c.dataset.aberto);
            var valor = Math.min(aberto, resto);
            c.value = valor > 0 ? escrever(valor) : "";
            resto = Math.round((resto - valor) * 100) / 100;
        });
        resumo();
    }

    form.addEventListener("input", resumo);
    form.addEventListener("click", function (e) {
        if (e.target.id === "distribuir") distribuir();
    });
    document.getElementById("valorRecebido").addEventListener("change", function () {
        // Se ainda não repartiu à mão, reparte automaticamente.
        if (campos().every(function (c) { return !c.value; })) distribuir();
        resumo();
    });

    // Cliente
    var resultados = document.getElementById("resultadosCliente");
    resultados.addEventListener("click", function (e) {
        var a = e.target.closest(".escolher-cliente");
        if (!a) return;
        e.preventDefault();
        document.getElementById("clienteId").value = a.dataset.id;
        document.getElementById("clienteNome").textContent = a.querySelector("span").textContent;
        document.getElementById("clienteEscolhido").classList.remove("d-none");
        document.getElementById("pesquisaClienteBloco").classList.add("d-none");
        resultados.innerHTML = "";
        htmx.trigger(document.body, "clienteEscolhido");
    });
    document.getElementById("trocarCliente").addEventListener("click", function () {
        document.getElementById("clienteEscolhido").classList.add("d-none");
        document.getElementById("pesquisaClienteBloco").classList.remove("d-none");
        document.getElementById("pesquisaCliente").focus();
    });

    document.body.addEventListener("htmx:afterSwap", function (e) {
        if (e.target.id === "abertos") {
            if (ler(document.getElementById("valorRecebido").value) > 0) distribuir();
            resumo();
        }
    });
    resumo();
})();
