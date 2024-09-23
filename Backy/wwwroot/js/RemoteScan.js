// Function to format size in bytes to human-readable format
function formatSize(sizeInBytes) {
    if (sizeInBytes === null || sizeInBytes === 0) return 'Unknown size';

    let size = sizeInBytes;
    const suffixes = ['B', 'KB', 'MB', 'GB', 'TB'];
    let suffixIndex = 0;

    while (size >= 1024 && suffixIndex < suffixes.length - 1) {
        size /= 1024;
        suffixIndex++;
    }

    return size.toFixed(2) + ' ' + suffixes[suffixIndex];
}

// Initialize tooltips
document.addEventListener('DOMContentLoaded', function () {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
});

// Handle expand/collapse chevron rotation
document.querySelectorAll('[data-bs-toggle="collapse"]').forEach(function (toggleButton) {
    var targetId = toggleButton.getAttribute('data-bs-target');
    var collapseElement = document.querySelector(targetId);

    if (collapseElement && toggleButton) {
        // Initialize collapsed state
        toggleButton.classList.add('collapsed');

        collapseElement.addEventListener('shown.bs.collapse', function () {
            toggleButton.classList.remove('collapsed');
        });

        collapseElement.addEventListener('hidden.bs.collapse', function () {
            toggleButton.classList.add('collapsed');
        });
    }
});

// Function to toggle enable/disable
function toggleEnable(id) {
    fetch(`/RemoteScan?handler=ToggleEnable&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
        .then(data => {
            location.reload();
        });
}

// Function to start indexing
function startIndexing(id) {
    fetch(`/RemoteScan?handler=StartIndexing&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
        .then(data => {
            alert('Indexing started.');
        });
}

// File Explorer functionality
let storageId = null;
let expandedPaths = new Set();

function openFileExplorer(selectedStorageId) {
    storageId = selectedStorageId;
    fetchFileExplorerData(storageId, null);
    $('#fileExplorerModal').modal('show');
}

function closeFileExplorer() {
    $('#fileExplorerModal').modal('hide');
}

function fetchFileExplorerData(storageId, path, searchQuery = '', highlightFile = '') {
    console.log('fetchFileExplorerData called with path:', path);
    $.ajax({
        url: '/RemoteScan?handler=GetFileExplorer',
        data: { storageId: storageId, path: path },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                renderFileExplorer(data, highlightFile);
            } else {
                alert('Error: ' + data.message);
            }
        },
        error: function () {
            alert('Error loading file explorer data.');
        }
    });
}

function renderFileExplorer(data, highlightFile = '') {
    const contentDiv = $('#fileExplorerContent');
    contentDiv.empty();

    const container = $('<div class="row"></div>');

    // Left column for directory navigation
    const leftCol = $('<div class="col-md-3 directory-navigation"></div>');
    const dirNav = $('<ul class="list-group"></ul>');

    // Assuming directoryTree is the root node
    const rootNode = data.directoryTree;
    if (rootNode && rootNode.children && rootNode.children.length > 0) {
        rootNode.children.forEach(function (childNode) {
            const childLi = renderDirectoryNode(childNode);
            dirNav.append(childLi);
        });
    }

    leftCol.append(dirNav);

    // Right column for files and breadcrumb
    const rightCol = $('<div class="col-md-9"></div>');

    // Breadcrumb
    const breadcrumb = $('<nav aria-label="breadcrumb"></nav>');
    const breadcrumbList = $('<ol class="breadcrumb align-items-center"></ol>');

    const navPathSegments = data.navPath.split('/').filter(s => s.length > 0);
    const remotePath = data.remotePath;
    let cumulativeNavPath = '';
    let cumulativeFullPath = remotePath;

    // Back button
    const backButton = $('<button class="btn btn-link p-0 mr-2" id="backButton"><img src="/icons/arrow-left-circle.svg" class="back-icon" alt="Back"></button>');
    backButton.click(function () {
        if (!backButton.find('img').hasClass('inactive-icon')) {
            fetchFileExplorerData(storageId, data.parentPath);
        }
    });
    breadcrumbList.append(backButton);

    // Root link
    const rootLi = $('<li class="breadcrumb-item"></li>');
    const rootLink = $('<a href="javascript:void(0);">Root</a>');
    rootLink.click(function () {
        fetchFileExplorerData(storageId, remotePath);
    });
    rootLi.append(rootLink);
    breadcrumbList.append(rootLi);

    navPathSegments.forEach((segment, index) => {
        cumulativeNavPath += '/' + segment;
        cumulativeFullPath += '/' + segment;
        const currentFullPath = cumulativeFullPath; // Capture the current value
        if (index === navPathSegments.length - 1) {
            breadcrumbList.append($('<li class="breadcrumb-item active" aria-current="page">' + segment + '</li>'));
        } else {
            const li = $('<li class="breadcrumb-item"></li>');
            const link = $('<a href="javascript:void(0);"></a>').text(segment);
            link.click(function () {
                fetchFileExplorerData(storageId, currentFullPath);
            });
            li.append(link);
            breadcrumbList.append(li);
        }
    });
    breadcrumb.append(breadcrumbList);
    rightCol.append(breadcrumb);

    // Update the back icon's opacity based on current path
    const backButtonImg = $('#backButton img');
    if (data.currentPath === data.remotePath) {
        backButtonImg.addClass('inactive-icon');
    } else {
        backButtonImg.removeClass('inactive-icon');
    }

    // File table
    const fileTable = $('<table class="table table-striped"></table>');
    const tableHeader = $('<thead><tr><th>Name</th><th>Type</th><th>Size</th><th>Backup</th></tr></thead>');
    fileTable.append(tableHeader);

    const tableBody = $('<tbody></tbody>');

    // Directories
    data.directories.forEach(dir => {
        const row = $('<tr></tr>');
        const nameCell = $('<td></td>');

        // Directory icon: use folder2-open.svg if directory is currently opened
        const isDirectoryOpen = expandedPaths.has(dir.fullPath);
        const dirIconSrc = isDirectoryOpen ? '/icons/folder2-open.svg' : '/icons/folder.svg';
        const dirIcon = $('<img src="' + dirIconSrc + '" class="directory-table-icon">');

        const link = $('<a href="javascript:void(0);"></a>').append(dirIcon).append(' ' + dir.name);
        link.click(function () {
            fetchFileExplorerData(storageId, dir.fullPath);
        });
        nameCell.append(link);
        row.append(nameCell);
        row.append('<td>Directory</td>');
        row.append('<td>N/A</td>');
        row.append('<td>N/A</td>');
        tableBody.append(row);
    });

    // Files
    data.files.forEach(file => {
        const row = $('<tr></tr>');
        const nameCell = $('<td></td>');

        // File icon
        const fileIcon = $('<img src="/icons/file-earmark.svg" class="file-table-icon">');

        nameCell.append(fileIcon).append(' ' + file.name);
        row.append(nameCell);
        row.append('<td>File</td>');
        row.append('<td>' + formatBytes(file.size) + '</td>');

        // Backup status
        let backupStatus;
        if (file.backupExists) {
            backupStatus = '<img src="/icons/check2.svg" class="badge-icon" alt="Yes">';
        } else {
            backupStatus = '<img src="/icons/x.svg" class="badge-icon" alt="No">';
        }
        row.append('<td>' + backupStatus + '</td>');

        if (file.name === highlightFile) {
            row.addClass('highlighted-file');
        }

        tableBody.append(row);
    });

    fileTable.append(tableBody);
    rightCol.append(fileTable);

    container.append(leftCol);
    container.append(rightCol);
    contentDiv.append(container);

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
}

function renderDirectoryNode(node, renderedPaths = new Set()) {
    // Check for cycles
    if (renderedPaths.has(node.fullPath)) {
        console.warn('Cycle detected for path:', node.fullPath);
        return $('<li class="list-group-item">Cycle detected: ' + node.name + '</li>');
    }

    // Mark the current path as rendered
    renderedPaths.add(node.fullPath);

    const li = $('<li class="list-group-item"></li>');

    // Directory icon
    let folderIcon;
    if (expandedPaths.has(node.fullPath)) {
        folderIcon = $('<img src="/icons/folder2-open.svg" class="directory-icon">');
    } else {
        folderIcon = $('<img src="/icons/folder.svg" class="directory-icon">');
    }

    // Declare childUl first
    let childUl;

    // Expand button (if the node has children)
    let expandButton;
    if (node.children && node.children.length > 0) {
        expandButton = $('<button class="btn btn-sm btn-link"><img src="/icons/chevron-right.svg" class="chevron-icon"></button>');
        li.append(expandButton);
    } else {
        // Add spacing for alignment if there's no expand button
        li.append('<span style="display:inline-block; width:16px;"></span>');
    }

    // Directory link with icon
    const link = $('<a href="javascript:void(0);"></a>').append(folderIcon).append(' ' + node.name);
    link.click(function () {
        fetchFileExplorerData(storageId, node.fullPath);
    });
    li.append(link);

    // Child nodes
    if (node.children && node.children.length > 0) {
        childUl = $('<ul class="list-group"></ul>');

        node.children.forEach(function (childNode) {
            const childLi = renderDirectoryNode(childNode, new Set(renderedPaths)); // Pass a new copy of renderedPaths
            childUl.append(childLi);
        });
        li.append(childUl);

        // Set initial visibility based on expandedPaths
        if (expandedPaths.has(node.fullPath)) {
            childUl.show();
            if (expandButton) expandButton.find('.chevron-icon').addClass('rotated');
            if (folderIcon) folderIcon.attr('src', '/icons/folder2-open.svg');
        } else {
            childUl.hide();
            if (expandButton) expandButton.find('.chevron-icon').removeClass('rotated');
            if (folderIcon) folderIcon.attr('src', '/icons/folder.svg');
        }

        // Attach click handler after childUl is defined
        if (expandButton) {
            expandButton.click(function (e) {
                e.stopPropagation(); // Prevent the click from triggering other events
                if (childUl.is(':visible')) {
                    childUl.hide();
                    expandButton.find('.chevron-icon').removeClass('rotated');
                    folderIcon.attr('src', '/icons/folder.svg'); // Change to closed folder
                    expandedPaths.delete(node.fullPath);
                } else {
                    childUl.show();
                    expandButton.find('.chevron-icon').addClass('rotated');
                    folderIcon.attr('src', '/icons/folder2-open.svg'); // Change to open folder
                    expandedPaths.add(node.fullPath);
                }
            });
        }
    }

    return li;
}

function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

// Search functionality
$('#searchInput').keyup(function () {
    const query = $(this).val();
    if (query.length >= 3) {
        searchFiles(query);
    } else {
        $('#searchSuggestions').hide();
    }
});

// Close suggestions when clicking outside
$(document).click(function (event) {
    if (!$(event.target).closest('#searchInput').length && !$(event.target).closest('#searchSuggestions').length && !$(event.target).closest('.input-group-append').length) {
        $('#searchSuggestions').hide();
    }
});

function searchFiles(query) {
    $.ajax({
        url: '/RemoteScan?handler=SearchFiles',
        data: { storageId: storageId, query: query },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                renderSearchSuggestions(data.results);
            } else {
                $('#searchSuggestions').hide();
            }
        },
        error: function () {
            $('#searchSuggestions').hide();
        }
    });
}

function renderSearchSuggestions(results) {
    const suggestionsDiv = $('#searchSuggestions');
    suggestionsDiv.empty();

    if (results.length === 0) {
        suggestionsDiv.hide();
        return;
    }

    results.forEach(function (result) {
        const item = $('<a class="dropdown-item" href="javascript:void(0);"></a>');
        item.text(result.name + ' (' + result.navPath + ')');
        item.click(function () {
            navigateToSearchResult(result);
            suggestionsDiv.hide();
        });
        suggestionsDiv.append(item);
    });

    suggestionsDiv.show();
}

function navigateToSearchResult(result) {
    $('#searchInput').val('');
    $('#searchSuggestions').hide();
    if (result.type === 'Directory') {
        // Navigate to the directory
        fetchFileExplorerData(storageId, result.fullPath);
    } else if (result.type === 'File') {
        // Navigate to the directory containing the file and highlight the file
        const directoryPath = result.fullPath.substring(0, result.fullPath.lastIndexOf('/'));
        fetchFileExplorerData(storageId, directoryPath, '', result.name);
    }
}
