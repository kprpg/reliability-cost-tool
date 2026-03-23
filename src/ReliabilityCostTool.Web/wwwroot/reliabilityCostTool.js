window.reliabilityCostTool = {
    downloadFile: function (fileName, contentType, base64Content) {
        const link = document.createElement('a');
        link.href = `data:${contentType};base64,${base64Content}`;
        link.download = fileName;
        link.click();
    }
};
