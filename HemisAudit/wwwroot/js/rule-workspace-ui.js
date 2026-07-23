(function () {
    var saveLockPrefix = 'ruleWorkspaceSaveLock:';

    function hasOwn(source, key) {
        return Object.prototype.hasOwnProperty.call(source || {}, key);
    }

    function getCurrentRuleNumber() {
        var match = window.location.pathname.match(/\/Rule(\d+)(?:\/|$)/i);
        return match && match[1] ? match[1] : '';
    }

    function getClientIdFromValue(value) {
        var parsed = parseInt(String(value || '').trim(), 10);
        return isFinite(parsed) && parsed > 0 ? parsed : 0;
    }

    function getCurrentClientId() {
        var clientInput = document.getElementById('clientId');
        if (clientInput) {
            var selectedClientId = getClientIdFromValue(clientInput.value);
            if (selectedClientId > 0) {
                return selectedClientId;
            }
        }

        var query = new URLSearchParams(window.location.search || '');
        return getClientIdFromValue(query.get('clientId'));
    }

    function buildSaveLockKey(ruleNumber, clientId) {
        return saveLockPrefix + String(ruleNumber || '') + ':' + String(clientId || 0);
    }

    function setWorkspaceSaveLocked(ruleNumber, clientId, isLocked) {
        if (!ruleNumber || !clientId || typeof window.localStorage === 'undefined') return;

        var key = buildSaveLockKey(ruleNumber, clientId);
        if (isLocked) {
            window.localStorage.setItem(key, '1');
        } else {
            window.localStorage.removeItem(key);
        }
    }

    function isWorkspaceSaveLocked(ruleNumber, clientId) {
        if (!ruleNumber || !clientId || typeof window.localStorage === 'undefined') return false;
        return window.localStorage.getItem(buildSaveLockKey(ruleNumber, clientId)) === '1';
    }

    function isCurrentWorkspaceSaveLocked() {
        return isWorkspaceSaveLocked(getCurrentRuleNumber(), getCurrentClientId());
    }

    function extractClientIdFromRequestOptions(requestOptions) {
        if (!requestOptions || typeof requestOptions.body !== 'string') return 0;

        try {
            var payload = JSON.parse(requestOptions.body);
            return getClientIdFromValue(payload && payload.clientId);
        } catch (_) {
            return 0;
        }
    }

    function extractClientIdFromResponsePayload(result) {
        if (!result || typeof result !== 'object') return 0;
        if (result.workspace) {
            return getClientIdFromValue(result.workspace.clientId);
        }

        return getClientIdFromValue(result.clientId);
    }

    function refreshWorkspaceSaveLock() {
        var saveButton = document.getElementById('saveWorkspaceBtn');
        if (!saveButton) return;

        var locked = isCurrentWorkspaceSaveLocked();
        saveButton.classList.toggle('is-workspace-save-locked', locked);

        if (locked) {
            saveButton.title = 'Run the validation again before saving this workspace again.';
        } else if (saveButton.title === 'Run the validation again before saving this workspace again.') {
            saveButton.title = '';
        }
    }

    var workspaceLoadState = {
        active: false,
        overlay: null,
        card: null,
        percentNode: null,
        metaNode: null,
        titleNode: null,
        fileNode: null,
        fillNode: null,
        observer: null,
        fallbackTimer: 0
    };

    function clearWorkspaceLoadFallback() {
        if (workspaceLoadState.fallbackTimer) {
            window.clearTimeout(workspaceLoadState.fallbackTimer);
            workspaceLoadState.fallbackTimer = 0;
        }
    }

    function normalizeWorkspaceLoadType(type) {
        var normalized = String(type || 'info').toLowerCase();
        if (normalized === 'danger') normalized = 'error';
        if (normalized !== 'success' && normalized !== 'warning' && normalized !== 'error') {
            normalized = 'info';
        }
        return normalized;
    }

    function ensureWorkspaceLoadOverlay() {
        if (workspaceLoadState.overlay && workspaceLoadState.card) {
            return workspaceLoadState;
        }

        var overlay = document.createElement('div');
        overlay.className = 'workspace-load-overlay';
        overlay.setAttribute('aria-hidden', 'true');
        overlay.innerHTML = [
            '<div class="workspace-load-card">',
            '<div class="workspace-load-card__body">',
            '<div class="workspace-load-card__gauge" aria-hidden="true">',
            '<div class="workspace-load-card__gauge-core">',
            '<div class="workspace-load-card__percent">0%</div>',
            '</div>',
            '</div>',
            '<div class="workspace-load-card__details">',
            '<div class="workspace-load-card__title">Loading workspace</div>',
            '<div class="workspace-load-card__file">Saved engagement workspace</div>',
            '<div class="workspace-load-card__meta">Restoring the last saved configuration and validation results...</div>',
            '</div>',
            '</div>',
            '<div class="workspace-load-card__bar"><span class="workspace-load-card__bar-fill"></span></div>',
            '</div>'
        ].join('');

        document.body.appendChild(overlay);

        workspaceLoadState.overlay = overlay;
        workspaceLoadState.card = overlay.querySelector('.workspace-load-card');
        workspaceLoadState.percentNode = overlay.querySelector('.workspace-load-card__percent');
        workspaceLoadState.metaNode = overlay.querySelector('.workspace-load-card__meta');
        workspaceLoadState.titleNode = overlay.querySelector('.workspace-load-card__title');
        workspaceLoadState.fileNode = overlay.querySelector('.workspace-load-card__file');
        workspaceLoadState.fillNode = overlay.querySelector('.workspace-load-card__bar-fill');
        return workspaceLoadState;
    }

    function updateWorkspaceLoadOverlay(percent, message, type) {
        var ui = ensureWorkspaceLoadOverlay();
        var normalizedPercent = Math.max(0, Math.min(100, Math.round(Number(percent) || 0)));
        var normalizedType = normalizeWorkspaceLoadType(type);

        ui.overlay.classList.add('is-visible');
        ui.overlay.setAttribute('aria-hidden', 'false');
        ui.card.classList.remove('is-success', 'is-warning', 'is-error', 'is-info', 'is-loading');
        ui.card.classList.add(normalizedPercent >= 100 ? 'is-' + normalizedType : 'is-loading');

        if (ui.percentNode) ui.percentNode.textContent = normalizedPercent + '%';
        if (ui.titleNode) ui.titleNode.textContent = 'Loading workspace';
        if (ui.fileNode) ui.fileNode.textContent = 'Saved engagement workspace';
        if (ui.metaNode) ui.metaNode.textContent = message || 'Restoring the last saved configuration and validation results...';
        if (ui.fillNode) ui.fillNode.style.width = normalizedPercent + '%';
        ui.card.style.setProperty('--progress-angle', (normalizedPercent * 3.6) + 'deg');
    }

    function disconnectWorkspaceLoadObserver() {
        if (workspaceLoadState.observer) {
            workspaceLoadState.observer.disconnect();
            workspaceLoadState.observer = null;
        }
    }

    function hideWorkspaceLoadOverlay(delay) {
        var ui = ensureWorkspaceLoadOverlay();
        window.setTimeout(function () {
            ui.overlay.classList.remove('is-visible');
            ui.overlay.setAttribute('aria-hidden', 'true');
        }, delay || 0);
    }

    function getWorkspaceInfoType() {
        var workspaceInfo = document.getElementById('workspaceInfo');
        if (!workspaceInfo) return 'info';

        var className = String(workspaceInfo.className || '').toLowerCase();
        if (className.indexOf('danger') >= 0 || className.indexOf('error') >= 0) return 'error';
        if (className.indexOf('warning') >= 0) return 'warning';
        if (className.indexOf('success') >= 0) return 'success';
        return 'info';
    }

    function isWorkspaceLoadPlaceholderText(text) {
        return /loading saved workspace|restoring the last saved configuration|workspace response received|finalizing the saved results/i.test(String(text || '').trim());
    }

    function evaluateWorkspaceLoadCompletion() {
        if (!workspaceLoadState.active) return;

        var workspaceInfo = document.getElementById('workspaceInfo');
        if (!workspaceInfo) return;

        var text = (workspaceInfo.textContent || '').trim();
        if (!text || isWorkspaceLoadPlaceholderText(text)) return;

        finishWorkspaceLoad(text, getWorkspaceInfoType());
    }

    function observeWorkspaceLoadCompletion() {
        if (workspaceLoadState.observer) {
            return;
        }

        var workspaceInfo = document.getElementById('workspaceInfo');
        if (!workspaceInfo || typeof MutationObserver !== 'function') {
            return;
        }

        workspaceLoadState.observer = new MutationObserver(function () {
            evaluateWorkspaceLoadCompletion();
        });

        workspaceLoadState.observer.observe(workspaceInfo, {
            childList: true,
            subtree: true,
            characterData: true,
            attributes: true,
            attributeFilter: ['class']
        });
    }

    function finishWorkspaceLoad(message, type) {
        clearWorkspaceLoadFallback();
        disconnectWorkspaceLoadObserver();

        var normalizedType = normalizeWorkspaceLoadType(type);
        var finalMessage = String(message || '').trim() || (normalizedType === 'error'
            ? 'Failed to load the saved workspace.'
            : 'Saved workspace loaded.');

        workspaceLoadState.active = false;
        updateWorkspaceLoadOverlay(100, finalMessage, normalizedType);

        if (normalizedType === 'success' || normalizedType === 'warning' || normalizedType === 'error') {
            window.showWorkspaceActionToast?.(finalMessage, normalizedType);
        }

        hideWorkspaceLoadOverlay(normalizedType === 'error' ? 1800 : 800);
    }

    function setWorkspaceLoadProgress(percent) {
        var normalizedPercent = Math.max(0, Math.min(100, Math.round(Number(percent) || 0)));
        workspaceLoadState.active = true;
        clearWorkspaceLoadFallback();
        observeWorkspaceLoadCompletion();

        updateWorkspaceLoadOverlay(
            normalizedPercent,
            normalizedPercent >= 100
                ? 'Saved workspace received. Finalizing the screen...'
                : 'Restoring the last saved configuration and validation results...',
            'info'
        );

        if (normalizedPercent >= 100) {
            workspaceLoadState.fallbackTimer = window.setTimeout(function () {
                if (!workspaceLoadState.active) return;
                finishWorkspaceLoad('Saved workspace loaded. The last analyst configuration and results are ready.', 'success');
            }, 2200);
        }
    }

    function syncWorkspaceSaveLock(url, method, requestOptions, result) {
        var normalizedUrl = String(url || '').toLowerCase();
        var normalizedMethod = String(method || 'GET').toUpperCase();
        var hasWorkspaceState = !!(result && typeof result === 'object' && result.workspace && typeof result.workspace === 'object');

        var ruleMatch = normalizedUrl.match(/\/rule(\d+)\//);
        var ruleNumber = ruleMatch && ruleMatch[1] ? ruleMatch[1] : getCurrentRuleNumber();
        var clientId = extractClientIdFromResponsePayload(result) || extractClientIdFromRequestOptions(requestOptions) || getCurrentClientId();
        if (!ruleNumber || !clientId) return;

        if (hasWorkspaceState && hasOwn(result.workspace, 'isWorkspaceSaved')) {
            setWorkspaceSaveLocked(ruleNumber, clientId, !!result.workspace.isWorkspaceSaved);
            refreshWorkspaceSaveLock();
            return;
        }

        if (normalizedMethod !== 'POST') return;

        if (normalizedUrl.indexOf('/saveworkspace') >= 0 && result && result.success) {
            setWorkspaceSaveLocked(ruleNumber, clientId, true);
            refreshWorkspaceSaveLock();
            return;
        }

        if ((normalizedUrl.indexOf('/runvalidation') >= 0 || normalizedUrl.indexOf('/beginworkspaceedit') >= 0) && result && result.success) {
            setWorkspaceSaveLocked(ruleNumber, clientId, false);
            refreshWorkspaceSaveLock();
        }
    }

    function resolveElement(target) {
        if (!target) return null;
        if (typeof target === 'string') {
            return document.getElementById(target) || document.querySelector(target);
        }
        return target;
    }

    function setFormSectionReadonly(target, isReadonly) {
        var root = resolveElement(target);
        if (!root) return null;

        root.classList.toggle('module-readonly-section', !!isReadonly);

        root.querySelectorAll('input, select, textarea, button').forEach(function (element) {
            if (element.type === 'hidden') return;

            if (isReadonly) {
                if (!hasOwn(element.dataset, 'ruleUiPrevDisabled')) {
                    element.dataset.ruleUiPrevDisabled = element.disabled ? '1' : '0';
                }

                if ('readOnly' in element && !hasOwn(element.dataset, 'ruleUiPrevReadonly')) {
                    element.dataset.ruleUiPrevReadonly = element.readOnly ? '1' : '0';
                }

                if ('disabled' in element) {
                    element.disabled = true;
                } else if ('readOnly' in element) {
                    element.readOnly = true;
                }

                return;
            }

            if (hasOwn(element.dataset, 'ruleUiPrevDisabled') && 'disabled' in element) {
                element.disabled = element.dataset.ruleUiPrevDisabled === '1';
                delete element.dataset.ruleUiPrevDisabled;
            }

            if (hasOwn(element.dataset, 'ruleUiPrevReadonly') && 'readOnly' in element) {
                element.readOnly = element.dataset.ruleUiPrevReadonly === '1';
                delete element.dataset.ruleUiPrevReadonly;
            }
        });

        return root;
    }

    function setReadonlyShell(target, isReadonly) {
        var element = resolveElement(target);
        if (!element) return null;

        element.classList.toggle('module-readonly', !!isReadonly);
        return element;
    }

    function matchesAllowSelector(element, selectors) {
        return (selectors || []).some(function (selector) {
            return !!selector && element.matches(selector);
        });
    }

    function resolveElements(target) {
        if (!target) return [];
        if (typeof target !== 'string') return target ? [target] : [];
        // CSS class/attribute selector — return ALL matches, not just the first
        if (target.charAt(0) === '.' || target.charAt(0) === '[') {
            return Array.from(document.querySelectorAll(target));
        }
        var el = document.getElementById(target) || document.querySelector(target);
        return el ? [el] : [];
    }

    function setButtonGroupReadonly(targets, isReadonly, options) {
        var allow = options && Array.isArray(options.allow) ? options.allow : [];
        var groups = Array.isArray(targets) ? targets : [targets];

        groups.forEach(function (target) {
            var roots = resolveElements(target);
            if (!roots.length) return;

            roots.forEach(function (root) {
                root.querySelectorAll('button').forEach(function (element) {
                    if (matchesAllowSelector(element, allow)) {
                        if (!isReadonly && hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                            element.disabled = element.dataset.ruleUiLockPrevDisabled === '1';
                            delete element.dataset.ruleUiLockPrevDisabled;
                        }

                        return;
                    }

                    if (isReadonly) {
                        if (!hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                            element.dataset.ruleUiLockPrevDisabled = element.disabled ? '1' : '0';
                        }

                        element.disabled = true;
                        return;
                    }

                    if (hasOwn(element.dataset, 'ruleUiLockPrevDisabled')) {
                        element.disabled = element.dataset.ruleUiLockPrevDisabled === '1';
                        delete element.dataset.ruleUiLockPrevDisabled;
                    }
                });
            });
        });
    }

    function applyState(config) {
        var element = resolveElement(config && (config.target || config.element || config.id));
        if (!element) return null;

        if (hasOwn(config, 'show')) {
            var displayValue = hasOwn(config, 'display') ? config.display : 'inline-flex';
            element.style.display = config.show ? displayValue : 'none';
        }

        if (hasOwn(config, 'disabled')) {
            element.disabled = !!config.disabled;
        }

        if (element.id === 'saveWorkspaceBtn' && isCurrentWorkspaceSaveLocked()) {
            if (!element.title) {
                element.title = 'Run the validation again before saving this workspace again.';
            }
            element.classList.add('is-workspace-save-locked');
        } else if (element.id === 'saveWorkspaceBtn') {
            element.classList.remove('is-workspace-save-locked');
            if (element.title === 'Run the validation again before saving this workspace again.') {
                element.title = '';
            }
        }

        if (hasOwn(config, 'text') && config.text != null) {
            element.textContent = config.text;
        }

        if (hasOwn(config, 'title')) {
            element.title = config.title || '';
        }

        return element;
    }

    function applyStates(configs) {
        (configs || []).forEach(applyState);
    }

    window.ruleWorkspaceUi = {
        resolveElement: resolveElement,
        setReadonlyShell: setReadonlyShell,
        setFormSectionReadonly: setFormSectionReadonly,
        setButtonGroupReadonly: setButtonGroupReadonly,
        applyState: applyState,
        applyStates: applyStates,
        setWorkspaceLoadProgress: setWorkspaceLoadProgress,
        finishWorkspaceLoad: finishWorkspaceLoad,
        refreshWorkspaceSaveLock: refreshWorkspaceSaveLock,
        syncWorkspaceSaveLock: syncWorkspaceSaveLock
    };

    document.addEventListener('change', function (event) {
        if (event.target && event.target.id === 'clientId') {
            refreshWorkspaceSaveLock();
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', refreshWorkspaceSaveLock);
    } else {
        refreshWorkspaceSaveLock();
    }
})();
