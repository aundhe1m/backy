// File: Backy/wwwroot/js/_FileExplorer.js

/**
 * File Explorer functionality
 */

// Variables to manage state
let storageId = null;
let expandedPaths = new Set();
let storageContentCache = null; // Cache for storage content
let currentNode = null; // Current node in the file explorer

// Variables to store sorting state
let sortColumn = 'name';
let sortOrder = 'asc';

/**
 * Opens the File Explorer modal for the selected storage.
 * @param {string} selectedStorageId - The ID of the selected storage.
 */
function openFileExplorer(selectedStorageId) {
    storageId = selectedStorageId;
    showLoadingSpinner(); // Show spinner when loading starts
    fetchFileExplorerData(storageId);
    $('#fileExplorerModal').modal('show');
}

/**
 * Closes the File Explorer modal and resets state variables.
 */
function closeFileExplorer() {
    $('#fileExplorerModal').modal('hide');
    expandedPaths.clear();
    storageContentCache = null;
    currentNode = null;
    hideLoadingSpinner(); // Ensure spinner is hidden
}

/**
 * Fetches File Explorer data from the server.
 * @param {string} storageId - The ID of the storage to fetch data for.
 */
function fetchFileExplorerData(storageId) {
    $.ajax({
        url: '/RemoteScan?handler=FileExplorer',
        data: { storageId: storageId },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                storageContentCache = data.storageContent;
                setParentReferences(storageContentCache); // Set parent references
                currentNode = storageContentCache;
                renderFileExplorer();
            } else {
                alert('Error: ' + data.message);
            }
            hideLoadingSpinner(); // Hide spinner after data is loaded
        },
        error: function () {
            alert('Error loading file explorer data.');
            hideLoadingSpinner(); // Hide spinner on error
        }
    });
}

/**
 * Recursively sets parent references for all nodes in the storage content.
 * @param {Object} node - The current node.
 * @param {Object|null} parent - The parent node.
 */
function setParentReferences(node, parent = null) {
    node.parent = parent;
    if (node.children) {
        node.children.forEach(child => {
            setParentReferences(child, node);
        });
    }
}

/**
 * Renders the File Explorer UI, including the directory tree and file table.
 * @param {string} [highlightFile=''] - The file name to highlight.
 */
function renderFileExplorer(highlightFile = '') {
    try {
        const contentDiv = $('#fileExplorerContent');
        contentDiv.empty();

        const container = $('<div class="row"></div>');

        // Left column for directory navigation
        const leftCol = $('<div class="col-md-3 directory-navigation"></div>');
        const dirNav = $('<ul class="list-group" id="directoryTree"></ul>');

        // Populate the directory tree with immediate children of root
        if (storageContentCache.children && storageContentCache.children.length > 0) {
            // Sort directories alphabetically
            const sortedChildren = storageContentCache.children.slice().sort((a, b) => a.name.localeCompare(b.name));
            sortedChildren.forEach(childNode => {
                if (childNode.type === 'directory') {
                    const childLi = buildDirectoryTree(childNode);
                    dirNav.append(childLi);
                }
            });
        }

        leftCol.append(dirNav);

        // Right column for files and breadcrumb
        const rightCol = $('<div class="col-md-9"></div>');

        // Breadcrumb and back button
        const breadcrumb = buildBreadcrumb();
        rightCol.append(breadcrumb);

        // Files table container with its own scrollbar
        const fileTableContainer = $('<div id="fileTableContainer" class="file-table-container"></div>');
        rightCol.append(fileTableContainer);
        fileTableContainer.append(buildFileTable(highlightFile));

        container.append(leftCol);
        container.append(rightCol);
        contentDiv.append(container);
    } catch (error) {
        console.error('Error rendering File Explorer:', error);
        alert('An error occurred while rendering the File Explorer. Please try again.');
        hideLoadingSpinner(); // Hide spinner on error
    }
}

/**
 * Builds the directory tree recursively.
 * @param {Object} node - The current directory node.
 * @returns {jQuery} - The list item representing the directory.
 */
function buildDirectoryTree(node) {
    const li = $('<li class="list-group-item"></li>');
    const fullPath = node.fullPath;

    // Directory icon
    let folderIcon = $('<img src="/icons/folder.svg" class="directory-icon">');

    // Check if directory has child directories
    const hasChildDirectories = node.children && node.children.some(child => child.type === 'directory');

    // Placeholder for alignment
    const spacer = $('<span style="display:inline-block; width:16px;"></span>');

    let expandButton;
    let childUl;

    // Expand button or spacer
    if (hasChildDirectories) {
        expandButton = $('<button class="btn btn-sm btn-link chevron-button"><img src="/icons/chevron-right.svg" class="chevron-icon"></button>');
        li.append(expandButton);
    } else {
        li.append(spacer);
    }

    // Directory link
    const link = $('<a href="javascript:void(0);"></a>').append(folderIcon).append(' ' + node.name);
    link.click(function () {
        currentNode = node;
        renderFileExplorer();
    });
    li.append(link);

    // Child nodes
    if (hasChildDirectories) {
        childUl = $('<ul class="list-group"></ul>');
        // Sort child directories alphabetically
        const sortedChildren = node.children.slice().sort((a, b) => a.name.localeCompare(b.name));
        sortedChildren.forEach(childNode => {
            if (childNode.type === 'directory') {
                const childLi = buildDirectoryTree(childNode);
                childUl.append(childLi);
            }
        });
        li.append(childUl);

        // Set initial visibility
        if (expandedPaths.has(fullPath)) {
            childUl.show();
            expandButton.find('.chevron-icon').addClass('rotated');
            folderIcon.attr('src', '/icons/folder2-open.svg');
        } else {
            childUl.hide();
            expandButton.find('.chevron-icon').removeClass('rotated');
            folderIcon.attr('src', '/icons/folder.svg');
        }

        // Toggle functionality
        expandButton.click(function (e) {
            e.stopPropagation();
            if (childUl.is(':visible')) {
                childUl.slideUp('fast');
                expandButton.find('.chevron-icon').removeClass('rotated');
                folderIcon.attr('src', '/icons/folder.svg');
                expandedPaths.delete(fullPath);
            } else {
                childUl.slideDown('fast');
                expandButton.find('.chevron-icon').addClass('rotated');
                folderIcon.attr('src', '/icons/folder2-open.svg');
                expandedPaths.add(fullPath);
            }
        });
    }

    return li;
}

/**
 * Builds the breadcrumb navigation based on the current node.
 * @returns {jQuery} - The breadcrumb navigation element.
 */
function buildBreadcrumb() {
    const breadcrumb = $('<nav aria-label="breadcrumb"></nav>');
    const breadcrumbList = $('<ol class="breadcrumb align-items-center"></ol>');

    // Back button
    const backButton = $('<button class="btn btn-link p-0 mr-2" id="backButton"><img src="/icons/arrow-left-circle.svg" class="back-icon" alt="Back"></button>');
    backButton.click(function () {
        if (currentNode.parent) {
            currentNode = currentNode.parent;
            renderFileExplorer();
        }
    });
    breadcrumbList.append(backButton);

    // Disable back button if at root
    if (!currentNode.parent) {
        backButton.prop('disabled', true);
        backButton.find('img').addClass('inactive-icon');
    } else {
        backButton.prop('disabled', false);
        backButton.find('img').removeClass('inactive-icon');
    }

    // Build breadcrumb items
    let node = currentNode;
    const nodes = [];
    while (node) {
        nodes.unshift(node);
        node = node.parent;
    }

    nodes.forEach((node, index) => {
        const li = $('<li class="breadcrumb-item"></li>');
        if (index === nodes.length - 1) {
            // Current node (active)
            li.text(node.name);
        } else {
            // Ancestor nodes (clickable)
            const link = $('<a href="javascript:void(0);"></a>').text(node.name);
            link.click(function () {
                currentNode = node;
                renderFileExplorer();
            });
            li.append(link);
        }
        breadcrumbList.append(li);
    });

    breadcrumb.append(breadcrumbList);
    return breadcrumb;
}

/**
 * Builds the file table with sortable columns.
 * @param {string} highlightFile - The file name to highlight.
 * @returns {jQuery} - The file table element.
 */
function buildFileTable(highlightFile) {
    const fileTable = $('<table class="table table-striped file-table"></table>');
    const tableHeader = $(`
        <thead>
            <tr>
                <th id="sort-name" class="sortable">Name <img src="/icons/caret-up-fill.svg" class="sort-icon"></th>
                <th id="sort-size" class="sortable">Size</th>
                <th id="sort-backup" class="sortable">Backup</th>
            </tr>
        </thead>
    `);
    fileTable.append(tableHeader);

    const tableBody = $('<tbody></tbody>');

    if (currentNode && currentNode.children) {
        // Combine directories and files
        let items = currentNode.children.map(item => {
            return {
                node: item,
                name: item.name,
                type: item.type,
                size: item.size,
                backupExists: item.backupExists,
                fullPath: item.fullPath
            };
        });

        // Sort the items
        items.sort((a, b) => {
            // Directories first
            if (a.type !== b.type) {
                return a.type === 'directory' ? -1 : 1;
            }

            let comparison = 0;
            if (sortColumn === 'name') {
                comparison = a.name.toLowerCase().localeCompare(b.name.toLowerCase());
            } else if (sortColumn === 'size') {
                comparison = (a.size || 0) - (b.size || 0);
            } else if (sortColumn === 'backup') {
                comparison = a.backupExists === b.backupExists ? 0 : a.backupExists ? -1 : 1;
            }

            return sortOrder === 'asc' ? comparison : -comparison;
        });

        items.forEach(item => {
            const row = $('<tr></tr>');
            const nameCell = $('<td></td>');

            if (item.type === 'directory') {
                // Directory icon and link
                const dirIcon = $('<img src="/icons/folder.svg" class="directory-table-icon">');
                const link = $('<a href="javascript:void(0);"></a>').append(dirIcon).append(' ' + item.name);
                link.click(function () {
                    currentNode = item.node;
                    renderFileExplorer();
                });
                nameCell.append(link);
            } else {
                // File icon
                const fileIcon = $('<img src="/icons/file-earmark.svg" class="file-table-icon">');
                nameCell.append(fileIcon).append(' ' + item.name);
            }

            row.append(nameCell);
            row.append('<td>' + (item.size !== undefined ? formatSize(item.size) : '') + '</td>');
            row.append('<td>' + (item.backupExists ? '<img src="/icons/check2.svg" class="badge-icon" alt="Yes">' : '<img src="/icons/x.svg" class="badge-icon" alt="No">') + '</td>');

            if (item.name === highlightFile) {
                row.addClass('highlighted-file');
            }

            tableBody.append(row);
        });
    }

    fileTable.append(tableBody);

    // Attach click handlers for sorting
    fileTable.find('#sort-name').click(function () {
        toggleSort('name');
        updateFileTable(highlightFile);
    });
    fileTable.find('#sort-size').click(function () {
        toggleSort('size');
        updateFileTable(highlightFile);
    });
    fileTable.find('#sort-backup').click(function () {
        toggleSort('backup');
        updateFileTable(highlightFile);
    });

    // Update sort icons
    updateSortIcons(fileTable);

    // Scroll to highlighted file
    if (highlightFile !== '') {
        const highlightedRow = tableBody.find('.highlighted-file');
        if (highlightedRow.length > 0) {
            highlightedRow[0].scrollIntoView({ behavior: 'smooth', block: 'center' });

            // Remove the highlight after 3 seconds
            setTimeout(function () {
                highlightedRow.removeClass('highlighted-file');
            }, 3000);
        }
    }

    return fileTable;
}

/**
 * Updates the file table by rebuilding it.
 * @param {string} highlightFile - The file name to highlight.
 */
function updateFileTable(highlightFile) {
    const fileTableContainer = $('#fileTableContainer');
    fileTableContainer.empty().append(buildFileTable(highlightFile));
}

/**
 * Toggles the sorting order for a given column.
 * @param {string} column - The column to sort by.
 */
function toggleSort(column) {
    if (sortColumn === column) {
        sortOrder = sortOrder === 'asc' ? 'desc' : 'asc';
    } else {
        sortColumn = column;
        sortOrder = 'asc';
    }
}

/**
 * Updates the sort icons in the table headers to reflect the current sorting state.
 * @param {jQuery} fileTable - The file table element.
 */
function updateSortIcons(fileTable) {
    fileTable.find('th img.sort-icon').remove();

    const sortIcon = $('<img class="sort-icon">').attr('src', sortOrder === 'asc' ? '/icons/caret-up-fill.svg' : '/icons/caret-down-fill.svg');

    if (sortColumn === 'name') {
        fileTable.find('#sort-name').append(' ').append(sortIcon);
    } else if (sortColumn === 'size') {
        fileTable.find('#sort-size').append(' ').append(sortIcon);
    } else if (sortColumn === 'backup') {
        fileTable.find('#sort-backup').append(' ').append(sortIcon);
    }
}

/**
 * Searches files and directories based on the query.
 * @param {string} query - The search query.
 * @param {boolean} [autoNavigate=false] - Whether to auto-navigate to the first result.
 */
function searchFiles(query, autoNavigate = false) {
    if (!storageContentCache) {
        return;
    }

    const results = [];
    const maxResults = 10;
    searchInNode(storageContentCache, query.toLowerCase(), results, maxResults);

    if (autoNavigate && results.length > 0) {
        navigateToSearchResult(results[0]);
    } else {
        renderSearchSuggestions(results);
    }
}

/**
 * Recursively searches within the node for matching files or directories.
 * @param {Object} node - The current node.
 * @param {string} query - The search query in lowercase.
 * @param {Array} results - The array to store search results.
 * @param {number} maxResults - The maximum number of results to collect.
 */
function searchInNode(node, query, results, maxResults) {
    if (results.length >= maxResults) {
        return;
    }

    if (node.name.toLowerCase().includes(query)) {
        results.push({
            node: node,
            type: node.type.charAt(0).toUpperCase() + node.type.slice(1),
            name: node.name,
            fullPath: node.fullPath
        });
    }

    if (node.children && results.length < maxResults) {
        for (let child of node.children) {
            searchInNode(child, query, results, maxResults);
            if (results.length >= maxResults) {
                break;
            }
        }
    }
}

/**
 * Renders search suggestions in the dropdown.
 * @param {Array} results - The search results to display.
 */
function renderSearchSuggestions(results) {
    const suggestionsDiv = $('#searchSuggestions');
    suggestionsDiv.empty();

    if (results.length === 0) {
        suggestionsDiv.hide();
        return;
    }

    results.forEach(function (result) {
        const item = $('<a class="dropdown-item" href="javascript:void(0);"></a>');
        let icon;
        if (result.type === "Directory") {
            icon = '<img src="/icons/folder.svg" class="directory-table-icon">';
        } else {
            icon = '<img src="/icons/file-earmark.svg" class="file-table-icon">';
        }
        const path = '<small class="text-muted"> [' + result.fullPath.replace(storageContentCache.fullPath, '') + ']</small>';
        item.html(icon + ' ' + result.name + ' ' + path);
        item.click(function () {
            navigateToSearchResult(result);
            suggestionsDiv.hide();
        });
        suggestionsDiv.append(item);
    });

    suggestionsDiv.show();
}

/**
 * Navigates to the selected search result.
 * @param {Object} result - The search result to navigate to.
 */
function navigateToSearchResult(result) {
    $('#searchInput').val('');
    $('#searchSuggestions').hide();
    if (result.type === 'Directory') {
        currentNode = result.node;
        renderFileExplorer();
    } else if (result.type === 'File') {
        currentNode = result.node.parent;
        renderFileExplorer(result.name);
    }
}

/**
 * Shows the loading spinner overlay.
 */
function showLoadingSpinner() {
    $('#fileExplorerSpinner').show();
}

/**
 * Hides the loading spinner overlay.
 */
function hideLoadingSpinner() {
    $('#fileExplorerSpinner').hide();
}

// Search functionality
$('#searchInput').keyup(function () {
    const query = $(this).val();
    if (query.length >= 1) {
        searchFiles(query);
    } else {
        $('#searchSuggestions').hide();
    }
});

// Handle Enter key press in search input
$('#searchInput').on('keypress', function (e) {
    if (e.which === 13) { // Enter key pressed
        e.preventDefault(); // Prevent default form submission
        const query = $(this).val();
        if (query.length >= 1) {
            searchFiles(query, true); // Auto-navigate to first result
        }
    }
});

// Handle search button click
$('#searchButton').click(function () {
    const query = $('#searchInput').val();
    if (query.length >= 1) {
        searchFiles(query, true); // Auto-navigate to first result
    }
});

// Close suggestions when clicking outside
$(document).click(function (event) {
    if (!$(event.target).closest('#searchInput').length && !$(event.target).closest('#searchSuggestions').length && !$(event.target).closest('.input-group-append').length) {
        $('#searchSuggestions').hide();
    }
});
