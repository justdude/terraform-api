(function () {
    'use strict';

    // ----------------------------------------------------------------
    // Tab switching
    // ----------------------------------------------------------------
    document.querySelectorAll('.toolbar-tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.toolbar-tab').forEach(function (t) { t.classList.remove('active'); });
            document.querySelectorAll('.tab-pane').forEach(function (p) { p.classList.remove('active'); });
            tab.classList.add('active');
            document.getElementById('pane-' + tab.dataset.tab).classList.add('active');
        });
    });

    // ----------------------------------------------------------------
    // Advanced settings toggle
    // ----------------------------------------------------------------
    var advToggle = document.getElementById('advanced-toggle');
    var advBody = document.getElementById('advanced-body');
    advToggle.addEventListener('click', function () {
        advToggle.classList.toggle('open');
        advBody.classList.toggle('open');
    });

    // ----------------------------------------------------------------
    // Product fields toggle
    // ----------------------------------------------------------------
    document.getElementById('generateProduct').addEventListener('change', function () {
        document.getElementById('product-fields').style.display = this.checked ? 'block' : 'none';
    });

    // ----------------------------------------------------------------
    // Environment presets — loaded from API, custom ones in localStorage
    // ----------------------------------------------------------------
    var LS_KEY = 'apim-custom-envs';
    var apiEnvConfigs = {};   // from server
    var envConfigs = {};      // merged (api + custom)

    function loadCustomEnvs() {
        try { return JSON.parse(localStorage.getItem(LS_KEY) || '{}'); }
        catch (e) { return {}; }
    }

    function saveCustomEnvs(obj) {
        localStorage.setItem(LS_KEY, JSON.stringify(obj));
    }

    function mergeEnvs() {
        var custom = loadCustomEnvs();
        envConfigs = Object.assign({}, apiEnvConfigs, custom);
    }

    function rebuildEnvSelect() {
        var select = document.getElementById('env-select');
        // Remove all options except the first placeholder
        while (select.options.length > 1) select.remove(1);
        Object.keys(envConfigs).forEach(function (key) {
            var opt = document.createElement('option');
            opt.value = key;
            opt.textContent = key.toUpperCase();
            select.appendChild(opt);
        });
    }

    fetch('/api/environments')
        .then(function (r) { return r.json(); })
        .then(function (data) {
            apiEnvConfigs = data;
            mergeEnvs();
            rebuildEnvSelect();
            renderEnvGrid();
        })
        .catch(function () { /* environments endpoint optional */ });

    document.getElementById('env-select').addEventListener('change', function () {
        var key = this.value;
        if (!key || !envConfigs[key]) return;
        applyEnvToSidebar(key, envConfigs[key]);
        setStatus('Loaded preset: ' + key.toUpperCase());
    });

    function applyEnvToSidebar(key, cfg) {
        setVal('environment', key);
        if (cfg.stageGroupName) setVal('stageGroupName', cfg.stageGroupName);
        if (cfg.apimName) setVal('apimName', cfg.apimName);
        if (cfg.apiGatewayHost) setVal('apiGatewayHost', cfg.apiGatewayHost);
        if (cfg.frontendHost) setVal('frontendHost', cfg.frontendHost);
        if (cfg.companyDomain) setVal('companyDomain', cfg.companyDomain);
        setVal('localDevHost', cfg.localDevHost || '');
        setVal('localDevPort', cfg.localDevPort || '');
        setChecked('subscriptionRequired', cfg.subscriptionRequired);
        setChecked('includeCorsPolicy', cfg.includeCorsPolicy);
    }

    // ---- Environments tab grid ----

    function renderEnvGrid() {
        var grid = document.getElementById('env-grid');
        if (!grid) return;
        grid.innerHTML = '';
        var custom = loadCustomEnvs();
        Object.keys(envConfigs).forEach(function (key) {
            grid.appendChild(buildEnvCard(key, envConfigs[key], !!custom[key]));
        });
    }

    function buildEnvCard(key, cfg, isCustom) {
        var card = document.createElement('div');
        card.className = 'env-card';
        card.dataset.envKey = key;

        var header = document.createElement('div');
        header.className = 'env-card-header';
        header.innerHTML =
            '<span class="env-card-name">' + esc(key) + '</span>' +
            '<span class="env-card-badge' + (isCustom ? ' custom' : '') + '">' +
            (isCustom ? 'custom' : 'config') + '</span>';
        card.appendChild(header);

        var body = document.createElement('div');
        body.className = 'env-card-body';

        var fields = [
            { id: 'stageGroupName', label: 'Resource Group', full: false },
            { id: 'apimName',       label: 'APIM Instance',  full: false },
            { id: 'apiGatewayHost', label: 'Gateway Host',   full: true },
            { id: 'frontendHost',   label: 'Frontend Host',  full: false },
            { id: 'companyDomain',  label: 'Company Domain', full: false },
            { id: 'localDevHost',   label: 'Dev Host',       full: false },
            { id: 'localDevPort',   label: 'Dev Port',       full: false }
        ];

        fields.forEach(function (f) {
            var wrap = document.createElement('div');
            wrap.className = 'env-card-field' + (f.full ? ' full' : '');
            var lbl = document.createElement('label');
            lbl.textContent = f.label;
            var inp = document.createElement('input');
            inp.type = 'text';
            inp.value = cfg[f.id] || '';
            inp.dataset.field = f.id;
            wrap.appendChild(lbl);
            wrap.appendChild(inp);
            body.appendChild(wrap);
        });

        card.appendChild(body);

        var checks = document.createElement('div');
        checks.className = 'env-card-checks';
        ['subscriptionRequired', 'includeCorsPolicy'].forEach(function (fid) {
            var lbl = document.createElement('label');
            var chk = document.createElement('input');
            chk.type = 'checkbox';
            chk.checked = !!cfg[fid];
            chk.dataset.field = fid;
            lbl.appendChild(chk);
            lbl.appendChild(document.createTextNode(' ' + (fid === 'subscriptionRequired' ? 'Subscription Required' : 'Include CORS')));
            checks.appendChild(lbl);
        });
        card.appendChild(checks);

        var footer = document.createElement('div');
        footer.className = 'env-card-footer';

        var applyBtn = document.createElement('button');
        applyBtn.className = 'btn-apply';
        applyBtn.textContent = 'Apply to Sidebar';
        applyBtn.addEventListener('click', function () {
            var saved = collectCardValues(card);
            applyEnvToSidebar(key, saved);
            setVal('environment', key);
            setStatus('Applied: ' + key.toUpperCase());
        });
        footer.appendChild(applyBtn);

        var saveBtn = document.createElement('button');
        saveBtn.className = 'btn btn-ghost';
        saveBtn.style.padding = '6px 10px';
        saveBtn.textContent = 'Save';
        saveBtn.addEventListener('click', function () {
            var custom = loadCustomEnvs();
            custom[key] = collectCardValues(card);
            saveCustomEnvs(custom);
            mergeEnvs();
            rebuildEnvSelect();
            renderEnvGrid();
            setStatus('Saved: ' + key.toUpperCase());
        });
        footer.appendChild(saveBtn);

        if (isCustom) {
            var delBtn = document.createElement('button');
            delBtn.className = 'btn-del';
            delBtn.textContent = 'Delete';
            delBtn.addEventListener('click', function () {
                var custom = loadCustomEnvs();
                delete custom[key];
                saveCustomEnvs(custom);
                mergeEnvs();
                rebuildEnvSelect();
                renderEnvGrid();
                setStatus('Deleted: ' + key.toUpperCase());
            });
            footer.appendChild(delBtn);
        }

        card.appendChild(footer);
        return card;
    }

    function collectCardValues(card) {
        var result = {};
        card.querySelectorAll('[data-field]').forEach(function (el) {
            result[el.dataset.field] = el.type === 'checkbox' ? el.checked : el.value;
        });
        return result;
    }

    // Add new environment
    document.getElementById('btn-env-add').addEventListener('click', function () {
        var name = prompt('Environment name (e.g. "uat"):');
        if (!name || !name.trim()) return;
        name = name.trim().toLowerCase();
        var custom = loadCustomEnvs();
        if (!custom[name]) custom[name] = {};
        saveCustomEnvs(custom);
        mergeEnvs();
        rebuildEnvSelect();
        renderEnvGrid();
        setStatus('Added: ' + name.toUpperCase());
    });

    // Reset custom environments to API defaults
    document.getElementById('btn-env-reset').addEventListener('click', function () {
        if (!confirm('Remove all custom environments and revert to config defaults?')) return;
        localStorage.removeItem(LS_KEY);
        mergeEnvs();
        rebuildEnvSelect();
        renderEnvGrid();
        setStatus('Environments reset to defaults');
    });

    // ----------------------------------------------------------------
    // File upload helpers
    // ----------------------------------------------------------------
    setupUpload('upload-convert', 'file-convert', 'input-convert');
    setupUpload('upload-update-api', 'file-update-api', 'input-update-api');
    setupUpload('upload-update-tf', 'file-update-tf', 'input-update-tf');
    setupUpload('upload-validate', 'file-validate', 'input-validate');

    function setupUpload(stripId, inputId, textareaId) {
        var strip = document.getElementById(stripId);
        var input = document.getElementById(inputId);
        var textarea = document.getElementById(textareaId);
        if (!strip || !input || !textarea) return;

        strip.addEventListener('click', function () { input.click(); });

        strip.addEventListener('dragover', function (e) {
            e.preventDefault();
            strip.classList.add('dragover');
        });
        strip.addEventListener('dragleave', function () { strip.classList.remove('dragover'); });
        strip.addEventListener('drop', function (e) {
            e.preventDefault();
            strip.classList.remove('dragover');
            if (e.dataTransfer.files[0]) readFile(e.dataTransfer.files[0], textarea);
        });
        input.addEventListener('change', function () {
            if (input.files[0]) readFile(input.files[0], textarea);
        });
    }

    function readFile(file, textarea) {
        var reader = new FileReader();
        reader.onload = function (e) {
            textarea.value = e.target.result;
            setStatus('Loaded file: ' + file.name);
        };
        reader.readAsText(file);
    }

    // ----------------------------------------------------------------
    // Gather settings from sidebar
    // ----------------------------------------------------------------
    function getSettings() {
        return {
            environment: val('environment'),
            apiGroupName: val('apiGroupName'),
            stageGroupName: val('stageGroupName'),
            apimName: val('apimName'),
            apiPathPrefix: val('apiPathPrefix'),
            apiPathSuffix: val('apiPathSuffix'),
            apiGatewayHost: val('apiGatewayHost'),
            backendServicePath: val('backendServicePath'),
            apiName: val('apiName') || null,
            apiDisplayName: null,
            apiVersion: val('apiVersion') || 'v1',
            revision: val('revision') || '1',
            productId: val('productId') || null,
            frontendHost: val('frontendHost') || null,
            companyDomain: val('companyDomain') || null,
            localDevHost: val('localDevHost') || null,
            localDevPort: val('localDevPort') || null,
            operationPrefix: val('operationPrefix') || null,
            includeCorsPolicy: getChecked('includeCorsPolicy'),
            subscriptionRequired: getChecked('subscriptionRequired'),
            allowedOrigins: [],
            allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS'],
            generateProduct: getChecked('generateProduct'),
            productDisplayName: val('productDisplayName') || null,
            productDescription: val('productDescription') || null,
            productSubscriptionRequired: getChecked('productSubscriptionRequired'),
            productApprovalRequired: getChecked('productApprovalRequired')
        };
    }

    function val(id) { var el = document.getElementById(id); return el ? el.value.trim() : ''; }
    function setVal(id, v) { var el = document.getElementById(id); if (el) el.value = v; }
    function getChecked(id) { var el = document.getElementById(id); return el ? el.checked : false; }
    function setChecked(id, v) { var el = document.getElementById(id); if (el) el.checked = !!v; }

    // ----------------------------------------------------------------
    // Status bar
    // ----------------------------------------------------------------
    function setStatus(text, isError) {
        document.getElementById('status-text').textContent = text;
        var dot = document.getElementById('status-dot');
        if (isError) { dot.classList.add('error'); } else { dot.classList.remove('error'); }
    }

    function setOps(count) {
        document.getElementById('status-ops').textContent = count != null ? count + ' operation(s)' : '';
    }

    // ----------------------------------------------------------------
    // Convert
    // ----------------------------------------------------------------
    document.getElementById('btn-convert').addEventListener('click', function () {
        var json = document.getElementById('input-convert').value.trim();
        if (!json) { setStatus('Error: No OpenAPI JSON provided', true); return; }
        var body = getSettings();
        body.openApiJson = json;
        callApi('/api/convert', body, 'output-convert', 'messages-convert');
    });

    // ----------------------------------------------------------------
    // Update
    // ----------------------------------------------------------------
    document.getElementById('btn-update').addEventListener('click', function () {
        var json = document.getElementById('input-update-api').value.trim();
        var tf = document.getElementById('input-update-tf').value.trim();
        if (!json) { setStatus('Error: No OpenAPI JSON provided', true); return; }
        if (!tf) { setStatus('Error: No existing Terraform provided', true); return; }
        var body = getSettings();
        body.openApiJson = json;
        body.existingTerraform = tf;
        callApi('/api/convert/update', body, 'output-update', 'messages-update');
    });

    // ----------------------------------------------------------------
    // Validate
    // ----------------------------------------------------------------
    document.getElementById('btn-validate').addEventListener('click', function () {
        var json = document.getElementById('input-validate').value.trim();
        if (!json) { setStatus('Error: No OpenAPI JSON provided', true); return; }
        var body = getSettings();
        body.openApiJson = json;

        setStatus('Validating...');
        var outEl = document.getElementById('output-validate');
        outEl.className = 'output-code idle';
        outEl.textContent = 'Validating...';

        fetch('/api/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
        .then(function (r) { return r.json(); })
        .then(function (data) { renderValidation(data, outEl); })
        .catch(function (err) {
            outEl.className = 'output-code has-error';
            outEl.textContent = 'Request failed: ' + err.message;
            setStatus('Validation failed', true);
        });
    });

    function renderValidation(data, el) {
        el.className = 'output-code';
        el.innerHTML = '';

        var wrap = document.createElement('div');
        wrap.className = 'validation-result';

        var h = document.createElement('h4');
        if (data.isValid) {
            h.className = 'valid';
            h.textContent = 'PASS - Valid OpenAPI specification';
            setStatus('Validation passed');
        } else {
            h.className = 'invalid';
            h.textContent = 'FAIL - Validation errors found';
            setStatus('Validation failed', true);
        }
        wrap.appendChild(h);

        if (data.errors && data.errors.length > 0) {
            var errUl = document.createElement('ul');
            errUl.className = 'op-list';
            data.errors.forEach(function (e) {
                var li = document.createElement('li');
                li.style.color = 'var(--red)';
                li.textContent = e;
                errUl.appendChild(li);
            });
            wrap.appendChild(errUl);
        }

        if (data.summary) {
            var info = document.createElement('p');
            info.style.marginTop = '12px';
            info.innerHTML = '<strong>' + esc(data.summary.apiName) + '</strong> &mdash; ' +
                data.summary.operationCount + ' operation(s)';
            wrap.appendChild(info);

            if (data.summary.operations && data.summary.operations.length > 0) {
                var ul = document.createElement('ul');
                ul.className = 'op-list';
                data.summary.operations.forEach(function (op) {
                    var li = document.createElement('li');
                    var badge = document.createElement('span');
                    badge.className = 'op-badge badge-' + op.method.toLowerCase();
                    badge.textContent = op.method;
                    var path = document.createElement('span');
                    path.className = 'op-path';
                    path.textContent = '/' + op.urlTemplate;
                    li.appendChild(badge);
                    li.appendChild(path);
                    ul.appendChild(li);
                });
                wrap.appendChild(ul);
            }
            setOps(data.summary.operationCount);
        }

        el.appendChild(wrap);
    }

    // ----------------------------------------------------------------
    // Shared API call for convert/update
    // ----------------------------------------------------------------
    function callApi(url, body, outputId, messagesId) {
        var outEl = document.getElementById(outputId);
        var msgEl = document.getElementById(messagesId);
        msgEl.innerHTML = '';
        outEl.className = 'output-code idle';
        outEl.textContent = 'Processing...';
        setStatus('Processing...');
        setOps(null);

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (data.success) {
                outEl.className = 'output-code';
                outEl.textContent = data.terraformConfig;

                if (data.summary) {
                    addMessage(msgEl, 'success',
                        '<strong>API:</strong> ' + esc(data.summary.displayName) +
                        ' &nbsp;|&nbsp; <strong>Path:</strong> ' + esc(data.summary.path) +
                        ' &nbsp;|&nbsp; <strong>Operations:</strong> ' + data.summary.operationCount);
                    setOps(data.summary.operationCount);
                }

                if (data.warnings && data.warnings.length > 0) {
                    addMessage(msgEl, 'warn',
                        '<strong>Warnings:</strong><ul>' +
                        data.warnings.map(function (w) { return '<li>' + esc(w) + '</li>'; }).join('') +
                        '</ul>');
                }

                setStatus('Conversion complete');
            } else {
                outEl.className = 'output-code has-error';
                outEl.textContent = '';
                if (data.errors && data.errors.length > 0) {
                    addMessage(msgEl, 'error',
                        '<strong>Errors:</strong><ul>' +
                        data.errors.map(function (e) { return '<li>' + esc(e) + '</li>'; }).join('') +
                        '</ul>');
                }
                setStatus('Conversion failed', true);
            }
        })
        .catch(function (err) {
            outEl.className = 'output-code has-error';
            outEl.textContent = '';
            addMessage(msgEl, 'error', '<strong>Request failed:</strong> ' + esc(err.message));
            setStatus('Request failed', true);
        });
    }

    function addMessage(container, type, html) {
        var div = document.createElement('div');
        div.className = 'msg-block msg-' + type;
        div.innerHTML = html;
        container.appendChild(div);
    }

    // ----------------------------------------------------------------
    // Copy / Download helpers
    // ----------------------------------------------------------------
    function setupCopy(btnId, outputId) {
        document.getElementById(btnId).addEventListener('click', function () {
            var text = document.getElementById(outputId).textContent;
            navigator.clipboard.writeText(text).then(function () {
                var btn = document.getElementById(btnId);
                var orig = btn.textContent;
                btn.textContent = 'Copied!';
                setTimeout(function () { btn.textContent = orig; }, 1500);
            });
        });
    }

    function setupDownload(btnId, outputId) {
        document.getElementById(btnId).addEventListener('click', function () {
            var text = document.getElementById(outputId).textContent;
            var blob = new Blob([text], { type: 'text/plain' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'apim-config.tf';
            a.click();
            URL.revokeObjectURL(url);
        });
    }

    setupCopy('btn-copy-convert', 'output-convert');
    setupCopy('btn-copy-update', 'output-update');
    setupDownload('btn-download-convert', 'output-convert');
    setupDownload('btn-download-update', 'output-update');

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------
    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }
})();
