
window.toggleAccordion = function (section) {
    const body = document.getElementById(`body-${section}`);
    if (body) body.style.display = body.style.display === 'none' ? '' : 'none';
};

(() => {
    const apiBasePath = '/Rule12';
    const currentSystemRole = 'DataAnalyst';
    const isResultsOnlyRole = false;
    const browserResultsRenderLimit = 10;
    const token = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]')?.value || '';
    const defaultCrseTable = 'dbo_QUAL';
    const state = {
        clientId: 0,
        currentRunId: null,
        summary: null,
        sql: '',
        isHydrating: false,
        resultsVisible: false,
        hasPendingValidation: false,
        currentUserEngagementRole: '',
        currentUserHasSignedOff: false,
        isWorkspaceSaved: false,
        hasDataAnalystSignoff: false,
        currentStatus: '',
        resultFilter: 'ALL',
        controlFilter: 'ALL',
        chartInstances: {}
    };
    let workspaceLoadToken = 0;

    const els = {
        clientId: document.getElementById('clientId'),
        server: document.getElementById('server'),
        driver: document.getElementById('driver'),
        database: document.getElementById('database'),
        cregTable: document.getElementById('cregTable'),
        qualTable: document.getElementById('qualTable'),
        cresTable: document.getElementById('cresTable'),
        cregStudentCol: document.getElementById('cregStudentCol'),
        cregQualCol: document.getElementById('cregQualCol'),
        cregCourseCol: document.getElementById('cregCourseCol'),
        cregExtra1Col: document.getElementById('cregExtra1Col'),
        cregExtra2Col: document.getElementById('cregExtra2Col'),
        cregFilterCol: document.getElementById('cregFilterCol'),
        applyCregFilter: document.getElementById('applyCregFilter'),
        cregFilterValues: document.getElementById('cregFilterValues'),
        cregExtra3Col: document.getElementById('cregExtra3Col'),
        qualJoinCol: document.getElementById('qualJoinCol'),
        qualDescCol: document.getElementById('qualDescCol'),
        cresCourseCol: document.getElementById('cresCourseCol'),
        cresStatusCol: document.getElementById('cresStatusCol'),
        cresExtra1Col: document.getElementById('cresExtra1Col'),
        cresStatusFilter: document.getElementById('cresStatusFilter'),
        connectionOutput: document.getElementById('connectionOutput'),
        configOutput: document.getElementById('configOutput'),
        analysisPane: document.getElementById('analysisPane'),
        resultsPane: document.getElementById('resultsPane'),
        chartsPane: document.getElementById('chartsPane'),
        sqlText: document.getElementById('sqlText'),
        downloadAllBtn: document.getElementById('downloadAllBtn'),
        workspaceInfo: document.getElementById('workspaceInfo'),
        workspaceRunState: document.getElementById('workspaceRunState'),
        workspaceRunLabel: document.getElementById('workspaceRunLabel'),
        workspaceStatusLabel: document.getElementById('workspaceStatusLabel'),
        workspaceEditedLabel: document.getElementById('workspaceEditedLabel'),
        workspaceRoleLabel: document.getElementById('workspaceRoleLabel'),
        workspaceSignoffComment: document.getElementById('workspaceSignoffComment'),
        beginEditBtn: document.getElementById('beginEditBtn'),
        saveWorkspaceBtn: document.getElementById('saveWorkspaceBtn'),
        signoffBtn: document.getElementById('signoffBtn'),
        removeSignoffBtn: document.getElementById('removeSignoffBtn'),
        statusBadge: document.getElementById('statusBadge')
    };

    const tabButtons = Array.from(document.querySelectorAll('.tab-btn'));
    tabButtons.forEach(btn => btn.addEventListener('click', () => setActiveTab(btn.dataset.tab)));

    function showSpinner(message) {
        document.getElementById('spinnerMsg').textContent = message || 'Processing...';
        document.getElementById('spinner').style.display = 'flex';
    }

    function hideSpinner() {
        document.getElementById('spinner').style.display = 'none';
    }

    function getActiveTab() {
        return document.querySelector('.tab-btn.active')?.dataset.tab || 'analysis';
    }

    function setActiveTab(tab) {
        tabButtons.forEach(btn => btn.classList.toggle('active', btn.dataset.tab === tab));
        document.querySelectorAll('.tab-pane').forEach(pane => {
            pane.classList.toggle('active', pane.dataset.tabPanel === tab);
        });

        if (tab === 'results') {
            window.setTimeout(() => renderResults(true), 0);
        } else if (tab === 'charts') {
            window.setTimeout(() => renderCharts(state.summary), 0);
        }
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString();
    }

    function normalizeCsvValues(value) {
        return String(value || '')
            .split(',')
            .map(part => part.trim().toUpperCase())
            .filter(Boolean)
            .join(',');
    }

    function updateCregFilterUi() {
        const isEnabled = !!els.applyCregFilter?.checked;
        if (els.cregFilterValues) {
            els.cregFilterValues.disabled = !isEnabled;
            els.cregFilterValues.placeholder = isEnabled ? 'N,E' : 'Tick "Apply CREG Filter" to enter values';
        }
    }

    function formatOptionalRule12Filter(summary) {
        const filterCol = String(summary?.cregFilterCol || '').trim();
        const filterValues = normalizeCsvValues(summary?.cregFilterValues || '');
        if (!filterCol) return 'Not mapped';
        if (!filterValues) return `${filterCol} mapped, not applied`;
        return `${filterCol} in (${filterValues.split(',').join(', ')})`;
    }

    function formatRule12MappedColumns(summary) {
        const cregColumns = [
            summary?.cregExtra1Col,
            summary?.cregExtra2Col,
            summary?.cregExtra3Col
        ].filter(Boolean);
        const cresColumns = [summary?.cresExtra1Col].filter(Boolean);
        const parts = [];
        if (cregColumns.length) parts.push(`CREG: ${cregColumns.join(', ')}`);
        if (cresColumns.length) parts.push(`CRES: ${cresColumns.join(', ')}`);
        return parts.join(' | ') || 'Core rule columns only';
    }

    function buildRule12ColumnGroups(summary) {
        const cregColumns = [
            { key: 'CREG__007', label: summary?.cregStudentCol || '_007', className: 'group-stud' },
            { key: 'CREG__001', label: summary?.cregQualCol || '_001', className: 'group-stud' },
            { key: 'CREG__030', label: summary?.cregCourseCol || '_030', className: 'group-stud' }
        ];

        if (summary?.cregExtra1Col) cregColumns.push({ key: 'CREG__EXTRA1', label: summary.cregExtra1Col, className: 'group-stud' });
        if (summary?.cregExtra2Col) cregColumns.push({ key: 'CREG__EXTRA2', label: summary.cregExtra2Col, className: 'group-stud' });
        if (summary?.cregFilterCol) cregColumns.push({ key: 'CREG__FILTER', label: summary.cregFilterCol, className: 'group-stud' });
        if (summary?.cregExtra3Col) cregColumns.push({ key: 'CREG__EXTRA3', label: summary.cregExtra3Col, className: 'group-stud' });

        const qualColumns = [
            { key: 'QUAL__001', label: summary?.qualJoinCol || '_001', className: 'group-bridge' },
            { key: 'QUAL__003', label: summary?.qualDescCol || '_003', className: 'group-bridge' }
        ];

        const cresColumns = [
            { key: 'CRES__030', label: summary?.cresCourseCol || '_030', className: 'group-final' },
            { key: 'CRES__031', label: summary?.cresStatusCol || '_031', className: 'group-final' }
        ];

        if (summary?.cresExtra1Col) cresColumns.push({ key: 'CRES__EXTRA1', label: summary.cresExtra1Col, className: 'group-final' });

        return { cregColumns, qualColumns, cresColumns };
    }

    function normalizeRole(value) {
        return String(value || '').replace(/[^a-z0-9]+/gi, '').toLowerCase();
    }

    function setAlert(target, type, message) {
        if (!target) return;
        target.innerHTML = `<div class="alert alert--${type}">${message}</div>`;
    }

    function setWorkspaceState(message, type, options = {}) {
        els.workspaceInfo.className = `alert alert--${type}`;
        if (options.allowHtml) {
            els.workspaceInfo.innerHTML = message || '';
        } else {
            els.workspaceInfo.textContent = message || '';
        }
        if (!options.suppressToast) {
            window.showWorkspaceActionToast?.(message, type);
        }
    }

    function setStatusBadge(text, type) {
        if (!text) {
            els.statusBadge.style.display = 'none';
            els.statusBadge.textContent = '';
            return;
        }

        els.statusBadge.style.display = 'inline-block';
        els.statusBadge.className = `rule-header__badge ${type === 'fail' ? 'badge--fail' : 'badge--pass'}`;
        els.statusBadge.textContent = text;
    }

    async function postJson(url, data) {
        return await window.fetchJsonWithProgress(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(data)
        });
    }

    async function getJson(url) {
        return await window.fetchJsonWithProgress(url, {
            method: 'GET'
        });
    }

    async function fetchWorkspaceState(clientId, includeSummary = false) {
        return await getJson(`${apiBasePath}/GetWorkspaceState?clientId=${encodeURIComponent(clientId)}&includeSummary=${includeSummary ? 'true' : 'false'}`);
    }

    async function downloadBlob(url, payload, defaultName) {
        const r = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(payload)
        });
        if (!r.ok) {
            let errorMessage = 'Download failed.';
            try {
                const responseText = await r.text();
                if (responseText) {
                    try {
                        const parsed = JSON.parse(responseText);
                        errorMessage = parsed?.error || parsed?.message || responseText || errorMessage;
                    } catch {
                        errorMessage = responseText || errorMessage;
                    }
                }
            } catch { }
            throw new Error(errorMessage);
        }
        const blob = await r.blob();
        const contentDisposition = r.headers.get('Content-Disposition') || '';
        const filenameMatch = contentDisposition.match(/filename\*?=(?:UTF-8'')?\"?([^\";]+)\"?/i);
        const resolvedName = filenameMatch?.[1] ? decodeURIComponent(filenameMatch[1]) : defaultName;
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = resolvedName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(a.href);
    }

    function fileStamp() {
        return new Date().toISOString().replace(/[:.]/g, '-').replace('T', '_').replace('Z', '');
    }

    function populateSelect(select, values, selected, placeholder = 'Select option') {
        if (!select) return;
        select.innerHTML = `<option value="">${escapeHtml(placeholder)}</option>`;
        values.forEach(value => {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = value;
            select.appendChild(option);
        });
        if (selected) {
            if (![...select.options].some(option => option.value === selected)) {
                const option = document.createElement('option');
                option.value = selected;
                option.textContent = selected;
                select.appendChild(option);
            }
            select.value = selected;
        }
    }

    function syncSelectValue(select, value, placeholder = 'Select option') {
        if (!select) return;
        if (!select.options.length) {
            select.innerHTML = `<option value="">${escapeHtml(placeholder)}</option>`;
        }
        if (!value) {
            select.value = '';
            return;
        }
        if (![...select.options].some(option => option.value === value)) {
            const option = document.createElement('option');
            option.value = value;
            option.textContent = value;
            select.appendChild(option);
        }
        select.value = value;
    }

    function getRequest() {
        const applyCregFilter = !!els.applyCregFilter?.checked;
        const cregFilterValues = applyCregFilter
            ? normalizeCsvValues(els.cregFilterValues?.value || '')
            : '';

        return {
            clientId:        parseInt(els.clientId.value || '0', 10) || 0,
            runId:           state.currentRunId,
            server:          els.server?.value.trim() || '',
            database:        els.database?.value || '',
            driver:          els.driver?.value || 'ODBC Driver 17 for SQL Server',
            cregTable:       els.cregTable?.value || 'dbo_CREG',
            qualTable:       els.qualTable?.value || 'dbo_QUAL',
            cresTable:       els.cresTable?.value || 'dbo_CRES',
            cregStudentCol:  els.cregStudentCol?.value || '_007',
            cregQualCol:     els.cregQualCol?.value || '_001',
            cregCourseCol:   els.cregCourseCol?.value || '_030',
            cregExtra1Col:   els.cregExtra1Col?.value || '',
            cregExtra2Col:   els.cregExtra2Col?.value || '',
            cregFilterCol:   els.cregFilterCol?.value || '',
            cregFilterValues,
            cregExtra3Col:   els.cregExtra3Col?.value || '',
            qualJoinCol:     els.qualJoinCol?.value || '_001',
            qualDescCol:     els.qualDescCol?.value || '_003',
            cresCourseCol:   els.cresCourseCol?.value || '_030',
            cresStatusCol:   els.cresStatusCol?.value || '_031',
            cresExtra1Col:   els.cresExtra1Col?.value || '',
            cresStatusFilter:(els.cresStatusFilter?.value || 'A').trim().toUpperCase()
        };
    }

    async function downloadExcelFile() {
        if (!state.summary && !state.currentRunId) {
            setWorkspaceState('Run Rule 12 first before downloading the Excel results.', 'warning');
            return;
        }

        await downloadBlob(
            `${apiBasePath}/DownloadExcel`,
            getRequest(),
            `Rule12_Course_Selection_${fileStamp()}.xlsx`
        );
    }

    async function downloadCsvFile() {
        if (!state.summary && !state.currentRunId) {
            setWorkspaceState('Run Rule 12 first before downloading the CSV results.', 'warning');
            return;
        }

        await downloadBlob(
            `${apiBasePath}/DownloadCsv`,
            getRequest(),
            `Rule12_Course_Selection_${fileStamp()}.csv`
        );
    }

    async function downloadSqlFile() {
        if ((currentSystemRole || '').toLowerCase() !== 'dataanalyst') {
            setWorkspaceState('Only the assigned data analyst can download the SQL script.', 'error');
            return;
        }

        await downloadBlob(
            `${apiBasePath}/DownloadSql`,
            getRequest(),
            `Rule12_Course_Selection_${fileStamp()}.sql`
        );
    }

    function fillColSel(selEl, cols, preferredCol) {
        if (!selEl) return;
        const cur = selEl.value;
        selEl.innerHTML = '';
        (cols || []).forEach(function(c) {
            const o = document.createElement('option');
            o.value = o.textContent = c;
            if (c === (preferredCol || cur)) o.selected = true;
            selEl.appendChild(o);
        });
        if (!selEl.value && cols.length) selEl.value = cols[0];
    }

    function fillOptionalColSel(selEl, cols, preferredCol, placeholder = 'Select optional column') {
        if (!selEl) return;
        const cur = selEl.value;
        const resolvedPreferredCol = preferredCol !== undefined ? preferredCol : cur;
        selEl.innerHTML = '';

        const blankOption = document.createElement('option');
        blankOption.value = '';
        blankOption.textContent = placeholder;
        selEl.appendChild(blankOption);

        (cols || []).forEach(function(c) {
            const o = document.createElement('option');
            o.value = o.textContent = c;
            if (c === resolvedPreferredCol) o.selected = true;
            selEl.appendChild(o);
        });

        if (!selEl.value) {
            selEl.value = resolvedPreferredCol && cols.includes(resolvedPreferredCol) ? resolvedPreferredCol : '';
        }
    }

    async function loadColumnsForTable(role, tableName, options = {}) {
        const server = els.server?.value?.trim();
        const db = els.database?.value;
        const resolvedTableName = tableName
            || (role === 'creg' ? els.cregTable?.value : role === 'qual' ? els.qualTable?.value : els.cresTable?.value);
        if (!server || !db || !resolvedTableName) return;
        try {
            const data = await postJson(`/Rule12/GetColumns`, {
                server, database: db,
                driver: els.driver?.value || 'ODBC Driver 17 for SQL Server',
                cregTable: resolvedTableName
            });
            if (!data.success || !data.columns) return;
            const cols = data.columns || [];
            if (role === 'creg') {
                fillColSel(els.cregStudentCol, cols, options.preferredStudentCol || '_007');
                fillColSel(els.cregQualCol,    cols, options.preferredQualCol || '_001');
                fillColSel(els.cregCourseCol,  cols, options.preferredCourseCol || '_030');
                fillOptionalColSel(els.cregExtra1Col, cols, options.preferredExtra1Col || '_064');
                fillOptionalColSel(els.cregExtra2Col, cols, options.preferredExtra2Col || '_032');
                fillOptionalColSel(els.cregFilterCol, cols, options.preferredFilterCol || '_051');
                fillOptionalColSel(els.cregExtra3Col, cols, options.preferredExtra3Col || '_018');
            } else if (role === 'qual') {
                fillColSel(els.qualJoinCol,  cols, options.preferredJoinCol || '_001');
                fillColSel(els.qualDescCol,  cols, options.preferredDescCol || '_003');
            } else if (role === 'cres') {
                fillColSel(els.cresCourseCol, cols, options.preferredCourseCol || '_030');
                fillColSel(els.cresStatusCol, cols, options.preferredStatusCol || '_031');
                fillOptionalColSel(els.cresExtra1Col, cols, options.preferredExtra1Col || '_058');
            }
            if (!options.silent) {
                setAlert(els.configOutput, 'success', `Columns loaded for ${resolvedTableName}.`);
            }
        } catch (e) { /* silently ignore */ }
    }

    window.detectRule12Columns = async function() {
        await Promise.all([
            loadColumnsForTable('creg', els.cregTable?.value),
            loadColumnsForTable('qual', els.qualTable?.value),
            loadColumnsForTable('cres', els.cresTable?.value)
        ]);
    };

    window.loadColumnsForTable = function(role, tableName) {
        return loadColumnsForTable(role, tableName);
    };

    function updateWorkspaceButtons() {
        const role = state.currentUserEngagementRole || currentSystemRole || '';
        const normalizedRole = normalizeRole(role);
        const normalizedSystemRole = normalizeRole(currentSystemRole);
        const hasRanValidation = !!state.summary;
        const canEdit = !isResultsOnlyRole &&
            (normalizedSystemRole === 'dataanalyst' || normalizedRole === 'dataanalyst');
        const canSaveWorkspace = hasRanValidation && canEdit;
        const canSign = !state.hasPendingValidation && !!state.currentRunId &&
            !!state.isWorkspaceSaved &&
            (normalizedRole === 'dataanalyst' || (state.resultsVisible && (normalizedRole === 'manager' || normalizedRole === 'director')));
        const signoffLocked = !!state.currentUserHasSignedOff;

        window.ruleWorkspaceUi.setReadonlyShell('app', !canEdit || signoffLocked);
        window.ruleWorkspaceUi.setFormSectionReadonly('.accordion-stack', !canEdit || signoffLocked);
        window.ruleWorkspaceUi.applyStates([
            { target: 'connectBtn', disabled: !canEdit || signoffLocked },
            { target: 'verifyBtn', disabled: !canEdit || signoffLocked },
            { target: 'runBtn', disabled: !canEdit || signoffLocked },
            { target: 'sqlBtn', disabled: !canEdit || signoffLocked },
            { target: els.beginEditBtn, disabled: signoffLocked || !canEdit || !state.currentRunId || state.hasPendingValidation },
            { target: els.saveWorkspaceBtn, disabled: signoffLocked || !canSaveWorkspace },
            {
                target: els.signoffBtn,
                disabled: signoffLocked || !canSign,
                text: state.currentUserHasSignedOff ? `Signed Off as ${role || currentSystemRole}` : 'Save Signoff'
            },
            { target: els.removeSignoffBtn, show: state.currentUserHasSignedOff, display: '', disabled: !state.currentUserHasSignedOff },
            { target: els.workspaceSignoffComment, disabled: signoffLocked || !canSign },
            { target: els.downloadAllBtn, disabled: !state.summary }
        ]);
        window.ruleWorkspaceUi.setButtonGroupReadonly(['.workspace-signoff-actions', '.action-bar'], signoffLocked, {
            allow: ['#downloadAllBtn', '#downloadExcelBtn', '#downloadCsvBtn', '#downloadSqlBtn', '#removeSignoffBtn']
        });
    }

    function destroyCharts() {
        Object.values(state.chartInstances || {}).forEach(chart => {
            if (chart && typeof chart.destroy === 'function') {
                chart.destroy();
            }
        });
        state.chartInstances = {};
    }

    function renderCharts(summary) {
        destroyCharts();

        if (!els.chartsPane) {
            return;
        }

        if (!summary) {
            els.chartsPane.innerHTML = '<div class="empty-card"><p>Charts will appear after running Rule 12.</p></div>';
            return;
        }

        if (typeof Chart === 'undefined') {
            els.chartsPane.innerHTML = '<div class="empty-card"><p>Charts could not load because Chart.js is unavailable.</p></div>';
            return;
        }

        const controls = summary.controlSummaries || [];
        const labels = controls.map(item => item.controlLabel || String(item.controlType || '').replace('_', ' '));
        const totalCounts = controls.map(item => Number(item.totalCount || 0));
        const passCounts = controls.map(item => Number(item.passCount || 0));
        const failCounts = controls.map(item => Number(item.failCount || 0));
        const successRates = controls.map(item => {
            const total = Number(item.totalCount || 0);
            return total > 0 ? Number(((Number(item.passCount || 0) / total) * 100).toFixed(2)) : 0;
        });

        els.chartsPane.innerHTML = `
            <div class="card" style="padding:20px">
                <h3 class="section-title" style="margin-top:0">Rule 12 Visual Summary</h3>
                <div class="charts-grid" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:16px">
                    <div class="chart-card">
                        <h4>Rows by Rule Outcome</h4>
                        <canvas id="Rule12ChartControlTotals"></canvas>
                    </div>
                    <div class="chart-card">
                        <h4>Overall PASS vs FAIL</h4>
                        <canvas id="Rule12ChartOutcome"></canvas>
                    </div>
                    <div class="chart-card">
                        <h4>Control Success Rate</h4>
                        <canvas id="Rule12ChartRates"></canvas>
                    </div>
                    <div class="chart-card chart-card--status ${summary.failCount > 0 ? 'chart-card--fail' : 'chart-card--pass'}">
                        <h4>Run Status</h4>
                        <div class="big-status">${escapeHtml(summary.status || '')}</div>
                        <div class="summary-text" style="width:100%;margin-top:12px">
                            <div><span>Selected Courses</span><strong>${formatNumber(summary.totalValidated)}</strong></div>
                            <div><span>Matched Courses</span><strong>${formatNumber(summary.passCount)}</strong></div>
                            <div><span>PASS</span><strong class="text-success">${formatNumber(summary.passCount)}</strong></div>
                            <div><span>FAIL</span><strong class="text-danger">${formatNumber(summary.failCount)}</strong></div>
                        </div>
                    </div>
                </div>
            </div>`;

        state.chartInstances.controlTotals = new Chart(document.getElementById('Rule12ChartControlTotals'), {
            type: 'bar',
            data: {
                labels,
                datasets: [
                    { label: 'PASS', data: passCounts, backgroundColor: '#4CAF50' },
                    { label: 'FAIL', data: failCounts, backgroundColor: '#F44336' }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'bottom' } },
                scales: { y: { beginAtZero: true } }
            }
        });

        state.chartInstances.outcome = new Chart(document.getElementById('Rule12ChartOutcome'), {
            type: 'doughnut',
            data: {
                labels: ['PASS', 'FAIL'],
                datasets: [{
                    data: [Number(summary.passCount || 0), Number(summary.failCount || 0)],
                    backgroundColor: ['#4CAF50', '#F44336'],
                    borderWidth: 0
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { position: 'bottom' } }
            }
        });

        state.chartInstances.rates = new Chart(document.getElementById('Rule12ChartRates'), {
            type: 'bar',
            data: {
                labels,
                datasets: [{
                    label: 'Success %',
                    data: successRates,
                    backgroundColor: ['#2563EB', '#0EA5E9', '#0F766E']
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: {
                    y: { beginAtZero: true, max: 100 }
                }
            }
        });
    }

    function normalizeBrowserSummary(summary) {
        if (!summary) return summary;
        const rows = Array.isArray(summary.reviewRows) ? summary.reviewRows : [];
        if (rows.length <= browserResultsRenderLimit) {
            return summary;
        }

        return {
            ...summary,
            reviewRows: rows.slice(0, browserResultsRenderLimit),
            displayedCount: Math.min(Number(summary.displayedCount || rows.length), browserResultsRenderLimit),
            isPreviewOnly: true,
            previewLimit: Number(summary.previewLimit || browserResultsRenderLimit)
        };
    }

    function applySummary(summary, options = {}) {
        state.summary = normalizeBrowserSummary(summary);
        if (!options.fromWorkspace) {
            state.currentRunId = summary?.savedRunId || state.currentRunId;
        }
        state.currentStatus = summary?.status || state.currentStatus;
        state.resultsVisible = !!summary || state.resultsVisible;

        if (!summary) {
            els.analysisPane.innerHTML = '<div class="empty-card"><p>No Rule 12 results loaded.</p></div>';
            els.resultsPane.innerHTML = '<div class="empty-card"><p>No Rule 12 results loaded.</p></div>';
            setStatusBadge('', 'pass');
            updateWorkspaceButtons();
            return;
        }

        if (state.hasPendingValidation) {
            els.workspaceRunState.textContent = `Pending save | ${summary.status || 'Completed'}`;
            els.workspaceRunLabel.textContent = 'Unsaved Rule 12 validation';
            els.workspaceStatusLabel.textContent = 'Pending Save';
        } else {
            els.workspaceRunState.textContent = state.currentRunId
                ? `Run #${state.currentRunId} | ${state.currentStatus || 'Completed'}`
                : 'No saved run loaded';
            els.workspaceRunLabel.textContent = state.currentRunId
                ? `Run #${state.currentRunId}`
                : 'No saved run yet';
            els.workspaceStatusLabel.textContent = state.currentStatus || '-';
        }
        if (!els.workspaceRoleLabel.textContent?.trim()) {
            els.workspaceRoleLabel.textContent = state.currentUserEngagementRole || currentSystemRole || '-';
        }

        setStatusBadge(summary.status, summary.failCount > 0 ? 'fail' : 'pass');
        renderAnalysis(summary);
        renderResults();
        renderCharts(summary);
        updateWorkspaceButtons();
    }

    function applyWorkspace(workspace, resultsVisible, preserveExistingSummary = false, options = {}) {
        const suppressToast = !!options.suppressToast;
        const applyStatusMessage = options.applyStatusMessage !== false;
        if (!workspace) {
            state.currentRunId = null;
            state.currentStatus = '';
            state.currentUserEngagementRole = '';
            state.currentUserHasSignedOff = false;
            state.isWorkspaceSaved = false;
            state.hasDataAnalystSignoff = false;
            state.resultsVisible = false;
            state.hasPendingValidation = false;
            els.workspaceRunState.textContent = 'No saved run loaded';
            els.workspaceRunLabel.textContent = 'No saved run yet';
            els.workspaceStatusLabel.textContent = '-';
            els.workspaceEditedLabel.textContent = '-';
            if (applyStatusMessage) {
                setWorkspaceState('No saved Rule 12 workspace exists for this engagement yet.', 'info', { suppressToast });
            }
            if (!preserveExistingSummary) {
                applySummary(null);
            } else {
                updateWorkspaceButtons();
            }
            return;
        }

        state.clientId = workspace.clientId || state.clientId;
        state.currentRunId = workspace.runId || null;
        state.currentStatus = workspace.currentStatus || '';
        state.currentUserEngagementRole = workspace.currentUserEngagementRole || currentSystemRole || '';
        state.currentUserHasSignedOff = !!workspace.currentUserHasSignedOff;
        state.isWorkspaceSaved = !!workspace.isWorkspaceSaved;
        state.hasDataAnalystSignoff = !!workspace.hasDataAnalystSignoff;
        state.resultsVisible = !!resultsVisible;
        state.hasPendingValidation = false;

        if (els.server) els.server.value = workspace.server || '';
        if (els.driver) els.driver.value = workspace.driver || 'ODBC Driver 17 for SQL Server';
        syncSelectValue(els.database, workspace.database, 'Select database');
        syncSelectValue(els.cregTable, workspace.cregTable, 'Select table');
        syncSelectValue(els.qualTable, workspace.qualTable || 'dbo_QUAL', 'Select table');
        syncSelectValue(els.cresTable, workspace.cresTable || 'dbo_CRES', 'Select table');
        syncSelectValue(els.cregStudentCol, workspace.cregStudentCol || '_007', 'Select column');
        syncSelectValue(els.cregQualCol, workspace.cregQualCol || '_001', 'Select column');
        syncSelectValue(els.cregCourseCol, workspace.cregCourseCol || '_030', 'Select column');
        syncSelectValue(els.cregExtra1Col, workspace.cregExtra1Col || '_064', 'Select optional column');
        syncSelectValue(els.cregExtra2Col, workspace.cregExtra2Col || '_032', 'Select optional column');
        syncSelectValue(els.cregFilterCol, workspace.cregFilterCol || '_051', 'Select optional column');
        syncSelectValue(els.cregExtra3Col, workspace.cregExtra3Col || '_018', 'Select optional column');
        syncSelectValue(els.qualJoinCol, workspace.qualJoinCol || '_001', 'Select column');
        syncSelectValue(els.qualDescCol, workspace.qualDescCol || '_003', 'Select column');
        syncSelectValue(els.cresCourseCol, workspace.cresCourseCol || '_030', 'Select column');
        syncSelectValue(els.cresStatusCol, workspace.cresStatusCol || '_031', 'Select column');
        syncSelectValue(els.cresExtra1Col, workspace.cresExtra1Col || '_058', 'Select optional column');
        if (els.cresStatusFilter) els.cresStatusFilter.value = workspace.cresStatusFilter || 'A';
        if (els.cregFilterValues) els.cregFilterValues.value = workspace.cregFilterValues || '';
        if (els.applyCregFilter) els.applyCregFilter.checked = !!String(workspace.cregFilterValues || '').trim();
        updateCregFilterUi();

        const runId = workspace.runId;
        if (state.currentUserHasSignedOff) {
            els.workspaceRunState.textContent = runId ? `Run #${runId} signed off` : 'No saved run loaded';
        } else if (state.hasDataAnalystSignoff) {
            els.workspaceRunState.textContent = runId ? `Run #${runId} analyst-signed` : 'No saved run loaded';
        } else {
            els.workspaceRunState.textContent = runId ? `Run #${runId} saved` : 'No saved run loaded';
        }
        els.workspaceRunLabel.textContent = runId ? `Rule 12 Run #${runId}` : 'No saved run yet';
        els.workspaceStatusLabel.textContent = workspace.currentStatus || '-';
        els.workspaceEditedLabel.textContent = workspace.lastEditedAt ? new Date(workspace.lastEditedAt).toLocaleString() : '-';
        els.workspaceRoleLabel.textContent = workspace.currentUserEngagementRole || currentSystemRole || '-';
        els.workspaceSignoffComment.value = workspace.currentUserSignoffComment || '';

        if (!resultsVisible) {
            if (applyStatusMessage) {
                setWorkspaceState('Saved Rule 12 results are not available for your engagement role.', 'info', { suppressToast });
            }
            if (!preserveExistingSummary) {
                applySummary(null);
            } else {
                updateWorkspaceButtons();
            }
            return;
        }

        if (applyStatusMessage) {
            if (state.currentUserHasSignedOff) {
                setWorkspaceState(`Saved run <strong>#${runId}</strong> is loaded with its last configuration and results. Your analyst signoff is already on this run.`, 'success', { suppressToast, allowHtml: true });
            } else if (state.hasDataAnalystSignoff) {
                setWorkspaceState(`Saved run <strong>#${runId}</strong> is loaded. Other assigned users can review this analyst-signed run.`, 'success', { suppressToast, allowHtml: true });
            } else {
                const canEdit = !isResultsOnlyRole && ['dataanalyst'].includes((state.currentUserEngagementRole || currentSystemRole || '').toLowerCase());
                setWorkspaceState(
                    runId
                        ? (canEdit
                            ? `Saved run <strong>#${runId}</strong> is loaded. The analyst signoff is still outstanding.`
                            : `Saved run <strong>#${runId}</strong> is loaded and is awaiting analyst signoff.`)
                        : 'Saved workspace loaded. The last analyst configuration and results are ready.',
                    'info',
                    { suppressToast, allowHtml: true }
                );
            }
        }

        if (workspace.summary) {
            applySummary(workspace.summary, { fromWorkspace: true });
        } else if (!preserveExistingSummary) {
            applySummary(null);
        } else {
            updateWorkspaceButtons();
        }
    }

    async function hydrateWorkspace(workspace, resultsVisible, loadToken) {
        state.isHydrating = true;
        let reconnectWarning = '';

        try {
            if (workspace.summary && resultsVisible) {
                applySummary(workspace.summary, { fromWorkspace: true });
            }

            if (workspace.server) {
                await connect({
                    preferredDatabase: workspace.database || '',
                    silent: true,
                    skipLoadTables: true,
                    statusMessage: workspace.database
                        ? `Connected to ${workspace.database}. Restored from saved run #${workspace.runId || '-'}.`
                        : 'Connected using the saved Rule 12 workspace.'
                });
            }

            if (loadToken !== workspaceLoadToken || state.clientId !== Number(els.clientId?.value || 0)) return;

            if (workspace.database) {
                await loadTables({
                    preferredDatabase: workspace.database || '',
                    preferredCregTable: workspace.cregTable || '',
                    preferredQualTable: workspace.qualTable || '',
                    preferredCresTable: workspace.cresTable || '',
                    preferredCregStudentCol: workspace.cregStudentCol || '',
                    preferredCregQualCol: workspace.cregQualCol || '',
                    preferredCregCourseCol: workspace.cregCourseCol || '',
                    preferredCregExtra1Col: workspace.cregExtra1Col || '',
                    preferredCregExtra2Col: workspace.cregExtra2Col || '',
                    preferredCregFilterCol: workspace.cregFilterCol || '',
                    preferredCregExtra3Col: workspace.cregExtra3Col || '',
                    preferredQualJoinCol: workspace.qualJoinCol || '',
                    preferredQualDescCol: workspace.qualDescCol || '',
                    preferredCresCourseCol: workspace.cresCourseCol || '',
                    preferredCresStatusCol: workspace.cresStatusCol || '',
                    preferredCresExtra1Col: workspace.cresExtra1Col || '',
                    silent: true
                });
            }
        } catch (error) {
            reconnectWarning = resultsVisible
                ? 'Saved workspace results are still loaded, but the live SQL source could not be reconnected.'
                : 'The saved workspace was found, but the live SQL source could not be reconnected.';
            setAlert(els.connectionOutput, 'warning', escapeHtml(error?.message || 'Live SQL reconnect failed.'));
        } finally {
            state.isHydrating = false;
            if (loadToken !== workspaceLoadToken || state.clientId !== Number(els.clientId?.value || 0)) return;
            applyWorkspace(workspace, resultsVisible, true, { suppressToast: true });
            if (reconnectWarning) {
                setWorkspaceState(reconnectWarning, 'warning', { suppressToast: true });
            }
        }
    }

    async function loadWorkspace() {
        state.clientId = Number(els.clientId?.value || 0);
        workspaceLoadToken += 1;
        const loadToken = workspaceLoadToken;
        if (!state.clientId) {
            applyWorkspace(null, false, false, { suppressToast: true });
            return;
        }

        setWorkspaceState('Loading saved workspace...', 'info', { suppressToast: true });
        const data = await fetchWorkspaceState(state.clientId, true);
        if (loadToken !== workspaceLoadToken || state.clientId !== Number(els.clientId?.value || 0)) return;
        if (!data.hasWorkspace || !data.workspace) {
            applyWorkspace(null, false, false, { suppressToast: true });
            return;
        }
        await hydrateWorkspace(data.workspace, data.resultsVisible, loadToken);
    }

    async function connect(options = {}) {
        const server = els.server?.value?.trim();
        if (!server) throw new Error('Enter the SQL Server name first.');

        const data = await postJson(`${apiBasePath}/GetDatabases`, {
            server,
            driver: els.driver.value
        });
        if (!data.success) throw new Error(data.error || 'Connection failed.');

        els.database.innerHTML = '<option value="">Select database</option>';
        data.databases.forEach(db => {
            const option = document.createElement('option');
            option.value = db;
            option.textContent = db;
            els.database.appendChild(option);
        });

        const preferredDatabase = options.preferredDatabase
            || ((data.databases || []).includes(els.database.value) ? els.database.value : '');
        if (preferredDatabase && (data.databases || []).includes(preferredDatabase)) {
            els.database.value = preferredDatabase;
        }

        setAlert(
            els.connectionOutput,
            'success',
            options.statusMessage || `Connected. Found ${data.databases.length} database(s).`
        );

        if (!options.skipLoadTables && els.database.value) {
            await loadTables({
                preferredDatabase: els.database.value,
                silent: !!options.silent
            });
        }
        return data;
    }

    async function loadTables(options = {}) {
        if (options.preferredDatabase) {
            syncSelectValue(els.database, options.preferredDatabase, 'Select database');
        }
        if (!els.database.value) return;

        const data = await postJson(`${apiBasePath}/GetTables`, {
            server: els.server.value.trim(),
            database: els.database.value,
            driver: els.driver.value
        });
        if (!data.success) throw new Error(data.error || 'Could not load tables.');

        const tables = data.tables || [];
        const selectedCregTable = options.preferredCregTable
            || (tables.includes(els.cregTable?.value) ? els.cregTable.value : '')
            || data.autoCregTable
            || data.autoStudTable
            || '';
        const selectedQualTable = options.preferredQualTable
            || (tables.includes(els.qualTable?.value) ? els.qualTable.value : '')
            || data.autoQualTable
            || '';
        const selectedCresTable = options.preferredCresTable
            || (tables.includes(els.cresTable?.value) ? els.cresTable.value : '')
            || data.autoCresTable
            || '';

        populateSelect(els.cregTable, tables, selectedCregTable, 'Select table');
        populateSelect(els.qualTable, tables, selectedQualTable, 'Select table');
        populateSelect(els.cresTable, tables, selectedCresTable, 'Select table');

        await Promise.all([
            selectedCregTable
                ? loadColumnsForTable('creg', selectedCregTable, {
                    preferredStudentCol: options.preferredCregStudentCol || '',
                    preferredQualCol: options.preferredCregQualCol || '',
                    preferredCourseCol: options.preferredCregCourseCol || '',
                    preferredExtra1Col: options.preferredCregExtra1Col || '',
                    preferredExtra2Col: options.preferredCregExtra2Col || '',
                    preferredFilterCol: options.preferredCregFilterCol || '',
                    preferredExtra3Col: options.preferredCregExtra3Col || '',
                    silent: true
                })
                : Promise.resolve(),
            selectedQualTable
                ? loadColumnsForTable('qual', selectedQualTable, {
                    preferredJoinCol: options.preferredQualJoinCol || '',
                    preferredDescCol: options.preferredQualDescCol || '',
                    silent: true
                })
                : Promise.resolve(),
            selectedCresTable
                ? loadColumnsForTable('cres', selectedCresTable, {
                    preferredCourseCol: options.preferredCresCourseCol || '',
                    preferredStatusCol: options.preferredCresStatusCol || '',
                    preferredExtra1Col: options.preferredCresExtra1Col || '',
                    silent: true
                })
                : Promise.resolve()
        ]);

        if (!options.silent) {
            setAlert(els.configOutput, 'success', `Loaded ${data.tables.length} table(s).`);
        }
        return data;
    }

    async function verifyTables() {
        const data = await postJson(`${apiBasePath}/VerifyTables`, getRequest());
        if (!data.success) throw new Error(data.error || 'Verification failed.');

        const optionalCregFilter = !!els.applyCregFilter?.checked && (els.cregFilterCol?.value || '') && normalizeCsvValues(els.cregFilterValues?.value || '');
        els.configOutput.innerHTML = `
            <div class="alert alert--success">
                CREG rows: <strong>${formatNumber(data.cregRecordCount)}</strong> |
                QUAL rows: <strong>${formatNumber(data.qualRecordCount)}</strong> |
                CRES active (${(els.cresStatusFilter?.value || 'A').toUpperCase()}): <strong>${formatNumber(data.cresActiveCount)}</strong> |
                Active students tested: <strong>${formatNumber(data.totalActiveStudents)}</strong> |
                Matched quals: <strong>${formatNumber(data.matchedQualCount)}</strong> |
                Missing quals: <strong>${formatNumber(data.missingQualCount)}</strong>
                ${optionalCregFilter ? `| CREG filter (${escapeHtml(els.cregFilterCol?.value || '')}): <strong>${escapeHtml(optionalCregFilter)}</strong>` : ''}
            </div>`;
    }

    async function runValidation() {
        const request = getRequest();
        if (!request.clientId) {
            setWorkspaceState('Select the approved engagement assigned to you before running this rule.', 'warning');
            return;
        }

        const data = await postJson(`${apiBasePath}/RunValidation`, request);
        if (!data.success) throw new Error(data.error || 'Rule 12 failed.');

        state.hasPendingValidation = true;
        state.currentRunId = null;
        state.currentStatus = data.status || '';
        state.currentUserHasSignedOff = false;
        state.isWorkspaceSaved = false;
        state.hasDataAnalystSignoff = false;
        state.resultsVisible = true;
        state.currentUserEngagementRole = state.currentUserEngagementRole || currentSystemRole || 'DataAnalyst';
        applySummary(data);

        const control1 = (data.controlSummaries || []).find(item => item.controlType === 'Control_1');
        setWorkspaceState(`Rule 12 validation completed. Active students tested: ${formatNumber(data.totalValidated)} | Matched qualifications: ${formatNumber(control1?.passCount || 0)} | Missing qualifications: ${formatNumber(control1?.failCount || 0)}. Click Save Workspace before signoff or visibility to other users.`, 'success');
    }

    async function generateSql() {
        const data = await postJson(`${apiBasePath}/GenerateSql`, getRequest());
        if (!data.success) throw new Error(data.error || 'SQL generation failed.');
        state.sql = data.sql || '';
        els.sqlText.value = state.sql;
        setActiveTab('sql');
    }

    async function beginEdit() {
        const data = await postJson(`${apiBasePath}/BeginWorkspaceEdit`, getRequest());
        if (!data.success) throw new Error(data.error || 'Could not begin editing.');
        const workspaceResultsVisible = Object.prototype.hasOwnProperty.call(data, 'resultsVisible')
            ? !!data.resultsVisible
            : !!data.workspace?.resultsVisible;
        applyWorkspace(data.workspace, workspaceResultsVisible, true, { applyStatusMessage: false, suppressToast: true });
        setWorkspaceState(data.message || 'Workspace ready for editing.', 'warning');
    }

    async function saveWorkspace() {
        const data = await postJson(`${apiBasePath}/SaveWorkspace`, getRequest());
        if (!data.success) throw new Error(data.error || 'Could not save workspace.');
        state.hasPendingValidation = false;
        const workspaceResultsVisible = Object.prototype.hasOwnProperty.call(data, 'resultsVisible')
            ? !!data.resultsVisible
            : !!data.workspace?.resultsVisible;
        applyWorkspace(data.workspace, workspaceResultsVisible, true, { applyStatusMessage: false, suppressToast: true });
        setWorkspaceState(data.message || 'Workspace saved.', data.signoffsCleared ? 'warning' : 'success');
    }

    async function signoffWorkspace() {
        const data = await postJson(`${apiBasePath}/SignOffWorkspace`, {
            clientId: state.clientId,
            runId: state.currentRunId,
            comment: els.workspaceSignoffComment.value.trim()
        });
        if (!data.success) throw new Error(data.error || 'Signoff failed.');
        state.currentUserHasSignedOff = true;
        const workspaceResultsVisible = Object.prototype.hasOwnProperty.call(data, 'resultsVisible')
            ? !!data.resultsVisible
            : !!data.workspace?.resultsVisible;
        applyWorkspace(data.workspace, workspaceResultsVisible, true, { applyStatusMessage: false, suppressToast: true });
        setWorkspaceState(data.message || 'Signoff saved.', 'success');
    }

    async function removeWorkspaceSignoff() {
        const previousRunId = state.currentRunId;
        const data = await postJson(`${apiBasePath}/RemoveWorkspaceSignoff`, {
            clientId: state.clientId,
            runId: state.currentRunId,
            comment: ''
        });
        if (!data.success) throw new Error(data.error || 'Could not remove signoff.');
        state.currentUserHasSignedOff = false;
        state.isWorkspaceSaved = false;
        const workspaceResultsVisible = Object.prototype.hasOwnProperty.call(data, 'resultsVisible')
            ? !!data.resultsVisible
            : !!data.workspace?.resultsVisible;
        const preserveExistingSummary = !!(data.workspace && previousRunId && data.workspace.runId === previousRunId);
        applyWorkspace(data.workspace, workspaceResultsVisible, preserveExistingSummary, { applyStatusMessage: false, suppressToast: true });
        setWorkspaceState(data.message || 'Signoff removed.', 'warning');
    }

    function renderAnalysis(summary) {
        if (!summary) {
            els.analysisPane.innerHTML = '<div class="empty-card"><p>No Rule 12 results loaded.</p></div>';
            return;
        }

        const control1 = (summary.controlSummaries || []).find(item => item.controlType === 'Control_1');

        const controlRows = (summary.controlSummaries || [])
            .map(item => `<tr><td>${escapeHtml(item.controlLabel)}</td><td>${escapeHtml(item.criteriaText)}</td><td>${item.totalCount.toLocaleString()}</td><td>${item.passCount.toLocaleString()}</td><td>${item.failCount.toLocaleString()}</td><td>${escapeHtml(item.status)}</td></tr>`)
            .join('');

        els.analysisPane.innerHTML = `
            ${summary.isPreviewOnly ? `<div class="alert alert--warning Rule14-preview-note">The browser shows a preview of ${summary.previewLimit.toLocaleString()} rows to stay responsive. Excel and CSV keep the full Rule 12 result set.</div>` : ''}
            <div class="Rule14-summary-grid">
                <div class="stat-card"><div class="stat-card__label">CREG Records</div><div class="stat-card__value">${(summary.cregRecordCount||0).toLocaleString()}</div></div>
                <div class="stat-card"><div class="stat-card__label">QUAL Records</div><div class="stat-card__value">${(summary.qualRecordCount||0).toLocaleString()}</div></div>
                <div class="stat-card"><div class="stat-card__label">CRES Active (${escapeHtml(summary.cresStatusFilter||'A')})</div><div class="stat-card__value">${(summary.cresActiveCount||0).toLocaleString()}</div></div>
                <div class="stat-card"><div class="stat-card__label">Active Students Tested</div><div class="stat-card__value">${summary.totalValidated.toLocaleString()}</div></div>
                <div class="stat-card"><div class="stat-card__label">Matched Qualifications</div><div class="stat-card__value">${summary.passCount.toLocaleString()}</div></div>
                <div class="stat-card stat-card--fail"><div class="stat-card__label">Missing Qualifications</div><div class="stat-card__value">${summary.failCount.toLocaleString()}</div></div>
            </div>
            <div class="alert alert--info" style="margin-top:16px">
                Active students tested: <strong>${formatNumber(control1?.totalCount || 0)}</strong> |
                Qualifications found in QUAL: <strong>${formatNumber(control1?.passCount || 0)}</strong> |
                Qualifications missing from QUAL: <strong>${formatNumber(control1?.failCount || 0)}</strong>
            </div>
            <div class="card" style="padding:20px;margin-top:20px">
                <h3 class="section-title" style="margin-top:0">Run Summary</h3>
                <div class="form-grid">
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">Database</div><div>${escapeHtml(summary.database)}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">Linkage</div><div style="font-size:12px">${escapeHtml(summary.tableLinkageText)}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">CREG Records</div><div>${(summary.cregRecordCount||0).toLocaleString()}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">QUAL Records</div><div>${(summary.qualRecordCount||0).toLocaleString()}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">CRES Active</div><div>${(summary.cresActiveCount||0).toLocaleString()}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">Status</div><div>${escapeHtml(summary.status)}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">Optional CREG Filter</div><div>${escapeHtml(formatOptionalRule12Filter(summary))}</div></div>
                    <div><div style="color:var(--text-muted);font-size:12px;text-transform:uppercase;font-weight:700">Mapped Optional Columns</div><div>${escapeHtml(formatRule12MappedColumns(summary))}</div></div>
                </div>
                <div class="Rule14-control-summary">
                    <table class="data-table">
                        <thead>
                            <tr><th>Control</th><th>Criteria</th><th>Rows Tested</th><th>PASS</th><th>FAIL</th><th>Status</th></tr>
                        </thead>
                        <tbody>${controlRows}</tbody>
                    </table>
                </div>
            </div>`;
    }

    function getFilteredRows(summary) {
        return (summary.reviewRows || []).filter(row => {
            const matchesControl = state.controlFilter === 'ALL' || row.controlType === state.controlFilter;
            const matchesResult = state.resultFilter === 'ALL' || String(row.validationResult || '').toUpperCase() === state.resultFilter;
            return matchesControl && matchesResult;
        });
    }

    function getControlHeading(controlType, criteriaText) {
        const number = String(controlType || '').replace('Control_', '').trim();
        return `CONTROL ${number}: ${criteriaText || ''}`;
    }

    function buildFinalMessage(resultText, criteriaText) {
        return `${resultText} this Criteria: ${criteriaText || ''}`;
    }

    function renderControlSection(summary, controlType) {
        const controlMeta = (summary.controlSummaries || []).find(item => item.controlType === controlType);
        const rows = getFilteredRows(summary).filter(row => row.controlType === controlType);
        if (state.controlFilter !== 'ALL' && state.controlFilter !== controlType) {
            return '';
        }

        const criteriaText = controlMeta?.criteriaText || '';
        const cregStudentCol  = summary.cregStudentCol  || '_007';
        const cregQualCol     = summary.cregQualCol     || '_001';
        const cregCourseCol   = summary.cregCourseCol   || '_030';
        const qualJoinCol     = summary.qualJoinCol     || '_001';
        const qualDescCol     = summary.qualDescCol     || '_003';
        const cresCourseCol   = summary.cresCourseCol   || '_030';
        const cresStatusCol   = summary.cresStatusCol   || '_031';
        const { cregColumns, qualColumns, cresColumns } = buildRule12ColumnGroups(summary);

        const rowHtml = rows.map(row => {
            const rawV = row.displayValues || {}; const v = Object.fromEntries(Object.entries(rawV).map(([k, val]) => [k.toUpperCase(), val]));
            const resultText = String(row.validationResult || '').toUpperCase() === 'FAIL' ? 'FAIL' : 'PASS';
            const isFail = resultText === 'FAIL';
            const cregCells = cregColumns.map(col => `<td class="${col.className}">${escapeHtml(v[col.key] || '')}</td>`).join('');
            const qualCells = qualColumns.map(col => `<td class="${col.className}">${escapeHtml(v[col.key] || '')}</td>`).join('');
            const cresCells = cresColumns.map(col => {
                const extraClass = col.key === 'CRES__031'
                    ? ` ${isFail ? 'is-fail' : 'is-pass'}`
                    : '';
                return `<td class="${col.className}${extraClass}">${escapeHtml(v[col.key] || '')}</td>`;
            }).join('');
            return `
                <tr>
                    ${cregCells}
                    ${qualCells}
                    ${cresCells}
                    <td class="final-cell ${isFail ? 'is-fail' : 'is-pass'}">
                        <div class="result-text">${escapeHtml(resultText)}</div>
                        <div class="message-text">${escapeHtml(v.FINAL_RESULT_MESSAGE || buildFinalMessage(resultText, criteriaText))}</div>
                    </td>
                </tr>`;
        }).join('');

        return `
            <div class="Rule14-control-card">
                <div class="Rule14-control-title">${escapeHtml(getControlHeading(controlType, criteriaText))}</div>
                ${rows.length === 0 ? `<div class="alert alert--info" style="margin:0">No rows match the selected control/result filters in the current browser result set.</div>` : `
                <div class="Rule14-table-wrap">
                    <table class="Rule14-grid">
                        <thead>
                            <tr>
                                <th class="group-stud" colspan="${cregColumns.length}">${escapeHtml(summary.cregTable || 'dbo_CREG')}</th>
                                <th class="group-bridge" colspan="${qualColumns.length}">${escapeHtml(summary.qualTable || 'dbo_QUAL')}</th>
                                <th class="group-final" colspan="${cresColumns.length}">${escapeHtml(summary.cresTable || 'dbo_CRES')}</th>
                                <th class="group-final">Final Results</th>
                            </tr>
                            <tr>
                                ${cregColumns.map(col => `<th class="${col.className}">${escapeHtml(col.label)}</th>`).join('')}
                                ${qualColumns.map(col => `<th class="${col.className}">${escapeHtml(col.label)}</th>`).join('')}
                                ${cresColumns.map(col => `<th class="${col.className}">${escapeHtml(col.label)}</th>`).join('')}
                                <th class="final-head">pass/fail</th>
                            </tr>
                        </thead>
                        <tbody>${rowHtml}</tbody>
                    </table>
                </div>`}
            </div>`;
    }

    function renderResults(force = false) {
        if (!state.summary) {
            els.resultsPane.innerHTML = '<div class="empty-card"><p>No Rule 12 results loaded.</p></div>';
            return;
        }

        if (!force && getActiveTab() !== 'results') {
            els.resultsPane.innerHTML = '<div class="empty-card"><p>Open the Results tab to load the browser preview.</p></div>';
            return;
        }

        const summary = state.summary;
        const filteredRows = getFilteredRows(summary);
        els.resultsPane.innerHTML = `
            <div class="Rule14-filterbar">
                <div class="field-group" style="margin:0;min-width:180px">
                    <label for="liveControlFilter">Control Filter</label>
                    <select id="liveControlFilter">
                        <option value="ALL">All controls</option>
                        <option value="Control_1">Control 1</option>
                    </select>
                </div>
                <div class="field-group" style="margin:0;min-width:180px">
                    <label for="liveResultFilter">Result Filter</label>
                    <select id="liveResultFilter">
                        <option value="PASS">PASS</option>
                        <option value="FAIL">FAIL</option>
                        <option value="ALL">All results</option>
                    </select>
                </div>
            </div>
            <div class="alert alert--info" style="margin-bottom:16px">
                Showing ${formatNumber(filteredRows.length)} loaded result row(s) in the screenshot layout.
            </div>
            <div class="Rule14-results-wrap">
                ${summary.isPreviewOnly ? `<div class="alert alert--warning">Only the saved browser preview is shown here. Downloads keep the saved Rule 12 result set.</div>` : ''}
                ${renderControlSection(summary, 'Control_1')}
            </div>`;

        const controlFilter = document.getElementById('liveControlFilter');
        const resultFilter = document.getElementById('liveResultFilter');
        if (controlFilter) controlFilter.value = state.controlFilter;
        if (resultFilter) resultFilter.value = state.resultFilter;

        controlFilter?.addEventListener('change', () => {
            state.controlFilter = controlFilter.value;
            renderResults(summary);
        });
        resultFilter?.addEventListener('change', () => {
            state.resultFilter = resultFilter.value;
            renderResults(summary);
        });
    }

    if (!isResultsOnlyRole) {
        els.applyCregFilter?.addEventListener('change', () => {
            updateCregFilterUi();
        });
        els.cregFilterValues?.addEventListener('input', () => {
            els.cregFilterValues.value = normalizeCsvValues(els.cregFilterValues.value);
        });
        els.database?.addEventListener('change', () => loadTables().catch(err => {
            els.configOutput.innerHTML = `<div class="alert alert--error">${escapeHtml(err.message)}</div>`;
        }));
        document.getElementById('connectBtn')?.addEventListener('click', () => connect().catch(err => {
            hideSpinner();
            els.connectionOutput.innerHTML = `<div class="alert alert--error">${escapeHtml(err.message)}</div>`;
        }));
        document.getElementById('verifyBtn')?.addEventListener('click', () => verifyTables().catch(err => {
            hideSpinner();
            els.configOutput.innerHTML = `<div class="alert alert--error">${escapeHtml(err.message)}</div>`;
        }));
        document.getElementById('runBtn')?.addEventListener('click', () => runValidation().catch(err => {
            hideSpinner();
            setWorkspaceState(err.message, 'error');
        }));
        document.getElementById('sqlBtn')?.addEventListener('click', () => generateSql().catch(err => {
            hideSpinner();
            els.sqlText.value = err.message;
        }));
        els.beginEditBtn?.addEventListener('click', () => beginEdit().catch(err => {
            hideSpinner();
            setWorkspaceState(err.message, 'error');
        }));
        els.saveWorkspaceBtn?.addEventListener('click', () => saveWorkspace().catch(err => {
            hideSpinner();
            setWorkspaceState(err.message, 'error');
        }));
        els.downloadAllBtn?.addEventListener('click', async () => {
            try {
                if (!state.summary && !state.currentRunId) throw new Error('Run Rule 12 first.');
                if (document.getElementById('chkExcel')?.checked) await downloadExcelFile();
                if (document.getElementById('chkCsv')?.checked) await downloadCsvFile();
                if (document.getElementById('chkSql')?.checked) await downloadSqlFile();
            } catch (err) {
                setWorkspaceState(err.message || 'Download failed.', 'error');
            }
        });
    }

    els.signoffBtn?.addEventListener('click', () => signoffWorkspace().catch(err => {
        hideSpinner();
        setWorkspaceState(err.message, 'error');
    }));
    els.removeSignoffBtn?.addEventListener('click', () => removeWorkspaceSignoff().catch(err => {
        hideSpinner();
        setWorkspaceState(err.message, 'error');
    }));
    document.getElementById('downloadExcelBtn')?.addEventListener('click', () => downloadExcelFile().catch(err => setWorkspaceState(err.message, 'error')));
    document.getElementById('downloadCsvBtn')?.addEventListener('click', () => downloadCsvFile().catch(err => setWorkspaceState(err.message, 'error')));
    document.getElementById('downloadSqlBtn')?.addEventListener('click', () => downloadSqlFile().catch(err => setWorkspaceState(err.message, 'error')));
    els.clientId?.addEventListener('change', () => loadWorkspace().catch(err => setWorkspaceState(err.message, 'error')));

    renderAnalysis(null);
    renderResults();
    renderCharts(null);
    updateCregFilterUi();
    updateWorkspaceButtons();
    if (state.clientId) {
        loadWorkspace().catch(err => setWorkspaceState(err.message, 'error'));
    } else if (isResultsOnlyRole) {
        setWorkspaceState('Select an engagement to load the saved Rule 12 results.', 'info');
    }
})();
