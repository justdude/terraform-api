(function () {
    'use strict';

    // Tab switching
    document.querySelectorAll('.tab').forEach(function (tab) {
        tab.addEventListener('click', function () {
            document.querySelectorAll('.tab').forEach(function (t) { t.classList.remove('active'); });
            document.querySelectorAll('.tab-content').forEach(function (c) { c.classList.remove('active'); });
            tab.classList.add('active');
            document.getElementById('tab-' + tab.dataset.tab).classList.add('active');
        });
    });

    // File upload handlers
    setupFileUpload('upload-area-convert', 'file-input-convert', 'openapi-input');
    setupFileUpload('upload-area-update', 'file-input-update', 'openapi-input-update');
    setupFileUpload('upload-area-terraform', 'file-input-terraform', 'terraform-input');
    setupFileUpload('upload-area-validate', 'file-input-validate', 'openapi-input-validate');

    function setupFileUpload(areaId, inputId, textareaId) {
        var area = document.getElementById(areaId);
        var input = document.getElementById(inputId);
        var textarea = document.getElementById(textareaId);

        if (!area || !input || !textarea) return;

        area.addEventListener('click', function () { input.click(); });

        area.addEventListener('dragover', function (e) {
            e.preventDefault();
            area.classList.add('dragover');
        });

        area.addEventListener('dragleave', function () {
            area.classList.remove('dragover');
        });

        area.addEventListener('drop', function (e) {
            e.preventDefault();
            area.classList.remove('dragover');
            var file = e.dataTransfer.files[0];
            if (file) readFile(file, textarea);
        });

        input.addEventListener('change', function () {
            if (input.files[0]) readFile(input.files[0], textarea);
        });
    }

    function readFile(file, textarea) {
        var reader = new FileReader();
        reader.onload = function (e) {
            textarea.value = e.target.result;
        };
        reader.readAsText(file);
    }

    // Collect settings from form
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
            includeCorsPolicy: checked('includeCorsPolicy'),
            subscriptionRequired: checked('subscriptionRequired'),
            allowedOrigins: [],
            allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS']
        };
    }

    function val(id) {
        var el = document.getElementById(id);
        return el ? el.value.trim() : '';
    }

    function checked(id) {
        var el = document.getElementById(id);
        return el ? el.checked : false;
    }

    // Convert form submit
    document.getElementById('settings-form').addEventListener('submit', function (e) {
        e.preventDefault();

        var openApiJson = document.getElementById('openapi-input').value.trim();
        if (!openApiJson) {
            showError('Please provide an OpenAPI JSON specification.');
            return;
        }

        var settings = getSettings();
        settings.openApiJson = openApiJson;

        callApi('/api/convert', settings);
    });

    // Update button
    document.getElementById('btn-update').addEventListener('click', function () {
        var openApiJson = document.getElementById('openapi-input-update').value.trim();
        var existingTerraform = document.getElementById('terraform-input').value.trim();

        if (!openApiJson) {
            showError('Please provide an OpenAPI JSON specification.');
            return;
        }
        if (!existingTerraform) {
            showError('Please provide existing Terraform configuration.');
            return;
        }

        var settings = getSettings();
        settings.openApiJson = openApiJson;
        settings.existingTerraform = existingTerraform;

        callApi('/api/convert/update', settings);
    });

    // Validate button
    document.getElementById('btn-validate').addEventListener('click', function () {
        var openApiJson = document.getElementById('openapi-input-validate').value.trim();
        if (!openApiJson) {
            showError('Please provide an OpenAPI JSON specification.');
            return;
        }

        var settings = getSettings();
        settings.openApiJson = openApiJson;

        fetch('/api/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        })
        .then(function (res) { return res.json(); })
        .then(function (data) { showValidationResults(data); })
        .catch(function (err) { showValidationError(err.message); });
    });

    function callApi(url, body) {
        var outputSection = document.getElementById('output-section');
        outputSection.classList.remove('hidden');

        document.getElementById('terraform-output').textContent = 'Converting...';
        hideMessages();

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
        .then(function (res) { return res.json(); })
        .then(function (data) { displayResult(data); })
        .catch(function (err) { displayApiError(err.message); });
    }

    function displayResult(data) {
        var outputEl = document.getElementById('terraform-output');
        var warningsEl = document.getElementById('warnings-area');
        var errorsEl = document.getElementById('errors-area');
        var summaryEl = document.getElementById('summary-area');

        hideMessages();

        if (data.success) {
            outputEl.textContent = data.terraformConfig;

            if (data.warnings && data.warnings.length > 0) {
                warningsEl.innerHTML = '<strong>Warnings:</strong><ul>' +
                    data.warnings.map(function (w) { return '<li>' + escapeHtml(w) + '</li>'; }).join('') +
                    '</ul>';
                warningsEl.classList.remove('hidden');
            }

            if (data.summary) {
                summaryEl.innerHTML =
                    '<strong>API:</strong> ' + escapeHtml(data.summary.displayName) +
                    ' | <strong>Path:</strong> ' + escapeHtml(data.summary.path) +
                    ' | <strong>Operations:</strong> ' + data.summary.operationCount;
                summaryEl.classList.remove('hidden');
            }
        } else {
            outputEl.textContent = '';
            if (data.errors && data.errors.length > 0) {
                errorsEl.innerHTML = '<strong>Errors:</strong><ul>' +
                    data.errors.map(function (e) { return '<li>' + escapeHtml(e) + '</li>'; }).join('') +
                    '</ul>';
                errorsEl.classList.remove('hidden');
            }
        }
    }

    function displayApiError(message) {
        var errorsEl = document.getElementById('errors-area');
        errorsEl.innerHTML = '<strong>Request Failed:</strong> ' + escapeHtml(message);
        errorsEl.classList.remove('hidden');
        document.getElementById('terraform-output').textContent = '';
    }

    function hideMessages() {
        document.getElementById('warnings-area').classList.add('hidden');
        document.getElementById('errors-area').classList.add('hidden');
        document.getElementById('summary-area').classList.add('hidden');
    }

    function showError(message) {
        var outputSection = document.getElementById('output-section');
        outputSection.classList.remove('hidden');
        hideMessages();
        var errorsEl = document.getElementById('errors-area');
        errorsEl.innerHTML = '<strong>Error:</strong> ' + escapeHtml(message);
        errorsEl.classList.remove('hidden');
        document.getElementById('terraform-output').textContent = '';
    }

    function showValidationResults(data) {
        var container = document.getElementById('validation-results');

        if (data.isValid) {
            var html = '<p class="valid"><strong>Valid OpenAPI specification</strong></p>';

            if (data.summary) {
                html += '<p style="margin-top:8px"><strong>' + escapeHtml(data.summary.apiName) +
                    '</strong> - ' + data.summary.operationCount + ' operation(s)</p>';
                html += '<ul>';
                data.summary.operations.forEach(function (op) {
                    var methodClass = 'method-' + op.method.toLowerCase();
                    html += '<li><span class="op-method ' + methodClass + '">' +
                        op.method + '</span> /' + escapeHtml(op.urlTemplate) + '</li>';
                });
                html += '</ul>';
            }

            container.innerHTML = html;
        } else {
            var errHtml = '<p class="invalid"><strong>Validation Failed</strong></p><ul>';
            data.errors.forEach(function (e) {
                errHtml += '<li style="color:var(--error)">' + escapeHtml(e) + '</li>';
            });
            errHtml += '</ul>';
            container.innerHTML = errHtml;
        }
    }

    function showValidationError(message) {
        document.getElementById('validation-results').innerHTML =
            '<p class="invalid">' + escapeHtml(message) + '</p>';
    }

    // Copy to clipboard
    document.getElementById('btn-copy').addEventListener('click', function () {
        var text = document.getElementById('terraform-output').textContent;
        navigator.clipboard.writeText(text).then(function () {
            var btn = document.getElementById('btn-copy');
            btn.textContent = 'Copied!';
            setTimeout(function () { btn.textContent = 'Copy to Clipboard'; }, 2000);
        });
    });

    // Download .tf file
    document.getElementById('btn-download').addEventListener('click', function () {
        var text = document.getElementById('terraform-output').textContent;
        var blob = new Blob([text], { type: 'text/plain' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = 'apim-config.tf';
        a.click();
        URL.revokeObjectURL(url);
    });

    function escapeHtml(str) {
        var div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
})();
