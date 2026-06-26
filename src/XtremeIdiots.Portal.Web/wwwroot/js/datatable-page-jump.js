// Shared DataTables UI helpers.
// Usage:
//   PortalDataTableUi.relocateSearch({ filtersContainerId: 'playersFilters', placeholder: 'Search players...' });
//   PortalDataTableUi.attachPageJump(tableApi, { label: 'Go to page' });
(function (window) {
    'use strict';

    function relocateSearch(options) {
        const settings = Object.assign({
            filtersContainerId: null,
            dataTableFilterId: 'dataTable_filter',
            placeholder: 'Search...',
            label: 'Search',
            inputId: null,
            inputClassName: 'form-control',
            resetButtonId: 'resetFilters',
            removePlaceholderGroupId: null
        }, options || {});

        const dtFilter = document.getElementById(settings.dataTableFilterId);
        if (!dtFilter) {
            return null;
        }

        const placeholderGroup = settings.removePlaceholderGroupId
            ? document.getElementById(settings.removePlaceholderGroupId)
            : null;

        let filters = settings.filtersContainerId
            ? document.getElementById(settings.filtersContainerId)
            : null;

        if (!filters && placeholderGroup && placeholderGroup.parentElement) {
            filters = placeholderGroup.parentElement;
        }

        if (!filters) {
            return null;
        }

        dtFilter.classList.add('filter-group');

        const input = dtFilter.querySelector('input');
        if (!input) {
            return null;
        }

        if (settings.inputClassName) {
            settings.inputClassName
                .split(' ')
                .filter(Boolean)
                .forEach(function (className) {
                    input.classList.add(className);
                });
        }

        input.placeholder = settings.placeholder;

        if (!input.id) {
            input.id = settings.inputId || (settings.dataTableFilterId + '_search');
        }

        const wrapperLabel = dtFilter.querySelector('label');
        if (wrapperLabel && wrapperLabel.contains(input)) {
            wrapperLabel.removeChild(input);
        }

        while (dtFilter.firstChild) {
            dtFilter.removeChild(dtFilter.firstChild);
        }

        const newLabel = document.createElement('label');
        newLabel.className = 'form-label';
        newLabel.setAttribute('for', input.id);
        newLabel.textContent = settings.label;

        dtFilter.appendChild(newLabel);
        dtFilter.appendChild(input);

        const resetBtn = settings.resetButtonId
            ? document.getElementById(settings.resetButtonId)
            : null;
        const resetGroup = resetBtn ? resetBtn.closest('.filter-group') : null;

        if (resetGroup && resetGroup.parentElement === filters) {
            filters.insertBefore(dtFilter, resetGroup);
        } else {
            filters.appendChild(dtFilter);
        }

        if (placeholderGroup) {
            placeholderGroup.remove();
        }

        return dtFilter;
    }

    function attachPageJump(table, options) {
        if (!table || typeof table.page !== 'function' || typeof table.table !== 'function') {
            return null;
        }

        const settings = Object.assign({
            label: 'Go to page',
            hideWhenSinglePage: true
        }, options || {});

        const tableNode = table.table().node();
        if (!tableNode || !tableNode.id) {
            return null;
        }

        const tableId = tableNode.id;
        const paginateId = tableId + '_paginate';
        const containerId = tableId + '_pageJumpContainer';
        const inputId = tableId + '_pageJumpInput';

        const paginateElement = document.getElementById(paginateId);
        if (!paginateElement || !paginateElement.parentElement) {
            return null;
        }

        const existingContainer = document.getElementById(containerId);
        if (existingContainer) {
            return existingContainer;
        }

        const container = document.createElement('div');
        container.id = containerId;
        container.className = 'dt-page-jump';

        const label = document.createElement('label');
        label.className = 'dt-page-jump__label';
        label.setAttribute('for', inputId);
        label.textContent = settings.label;

        const input = document.createElement('input');
        input.type = 'number';
        input.id = inputId;
        input.className = 'form-control form-control-sm dt-page-jump__input';
        input.min = '1';
        input.step = '1';
        input.setAttribute('inputmode', 'numeric');

        const total = document.createElement('span');
        total.className = 'dt-page-jump__total';
        total.setAttribute('aria-live', 'polite');

        container.appendChild(label);
        container.appendChild(input);
        container.appendChild(total);

        paginateElement.parentElement.insertBefore(container, paginateElement);
        const eventNamespace = '.dt.pageJump.' + tableId;

        function updateState() {
            const pageInfo = table.page.info();
            if (!pageInfo) {
                return;
            }

            const currentPage = pageInfo.page + 1;
            const totalPages = pageInfo.pages;

            input.value = currentPage;
            input.max = totalPages;
            total.textContent = 'of ' + totalPages;

            const shouldHide = settings.hideWhenSinglePage && totalPages <= 1;
            container.style.display = shouldHide ? 'none' : '';
            input.disabled = totalPages <= 1;
        }

        function navigateToInputPage() {
            const pageInfo = table.page.info();
            if (!pageInfo || pageInfo.pages <= 0) {
                return;
            }

            const requestedPage = Number.parseInt(input.value, 10);
            if (Number.isNaN(requestedPage) || requestedPage < 1 || requestedPage > pageInfo.pages) {
                input.value = pageInfo.page + 1;
                return;
            }

            table.page(requestedPage - 1).draw(false);
        }

        function onInputKeyDown(event) {
            if (event.key !== 'Enter') {
                return;
            }

            event.preventDefault();
            navigateToInputPage();
        }

        input.addEventListener('change', navigateToInputPage);
        input.addEventListener('blur', navigateToInputPage);
        input.addEventListener('keydown', onInputKeyDown);

        table.off('draw' + eventNamespace);
        table.off('destroy' + eventNamespace);

        table.on('draw' + eventNamespace, updateState);
        table.on('destroy' + eventNamespace, function () {
            input.removeEventListener('change', navigateToInputPage);
            input.removeEventListener('blur', navigateToInputPage);
            input.removeEventListener('keydown', onInputKeyDown);
            container.remove();
        });

        updateState();
        return container;
    }

    window.PortalDataTableUi = window.PortalDataTableUi || {};
    window.PortalDataTableUi.relocateSearch = relocateSearch;
    window.PortalDataTableUi.attachPageJump = attachPageJump;
})(window);
