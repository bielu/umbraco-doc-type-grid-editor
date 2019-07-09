﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Web.Http;
using System.Web.Http.ModelBinding;
using Our.Umbraco.DocTypeGridEditor.Extensions;
using Our.Umbraco.DocTypeGridEditor.Helpers;
using Our.Umbraco.DocTypeGridEditor.Models;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Services;
using Umbraco.Web;
using Umbraco.Web.Composing;
using Umbraco.Web.Editors;
using Umbraco.Web.Mvc;
using Umbraco.Web.PublishedCache;

namespace Our.Umbraco.DocTypeGridEditor.Web.Controllers
{
    [PluginController("DocTypeGridEditorApi")]
    public class DocTypeGridEditorApiController : UmbracoAuthorizedJsonController
    {
        private readonly IUmbracoContextAccessor _umbracoContext;
        private readonly IContentTypeService _contentTypeService;
        private readonly IDataTypeService _dataTypeService;
        private readonly IPublishedContentCache _contentCache;

        public DocTypeGridEditorApiController()
        {
        }

        public DocTypeGridEditorApiController(IUmbracoContextAccessor umbracoContext,
            IContentTypeService contentTypeService,
            IDataTypeService dataTypeService,
            IPublishedContentCache contentCache)
        {
            _umbracoContext = umbracoContext;
            _contentTypeService = contentTypeService;
            _dataTypeService = dataTypeService;
            _contentCache = contentCache;
        }

        [HttpGet]
        public object GetContentTypeAliasByGuid([ModelBinder] Guid guid)
        {
            return new
            {
                alias = _contentTypeService.GetAliasByGuid(guid)
            };
        }

        [HttpGet]
        public IEnumerable<object> GetContentTypes([ModelBinder] string[] allowedContentTypes)
        {
            var allContentTypes = Current.Services.ContentTypeService.GetAll().ToList();
            var contentTypes = allContentTypes
                .Where(x => x.IsElement)
                .Where(x => allowedContentTypes == null || allowedContentTypes.Length == 0 || allowedContentTypes.Any(y => Regex.IsMatch(x.Alias, y)))
                .OrderBy(x => x.Name)
                .ToList();

            var blueprints = Current.Services.ContentService.GetBlueprintsForContentTypes(contentTypes.Select(x => x.Id).ToArray()).ToArray();

            return contentTypes
                .Select(x => new
                {
                    id = x.Id,
                    guid = x.Key,
                    name = x.Name,
                    alias = x.Alias,
                    description = x.Description,
                    icon = x.Icon,
                    blueprints = blueprints.Where(bp => bp.ContentTypeId == x.Id).Select(bp => new
                    {
                        id = bp.Id,
                        name = bp.Name
                    })
                });
        }

        [HttpGet]
        public object GetContentType([ModelBinder] string contentTypeAlias)
        {
            Guid docTypeGuid;
            if (Guid.TryParse(contentTypeAlias, out docTypeGuid))
                contentTypeAlias = Current.Services.ContentTypeService.GetAliasByGuid(docTypeGuid);

            var contentType = Current.Services.ContentTypeService.Get(contentTypeAlias);
            return new
            {
                icon = contentType != null ? contentType.Icon : "icon-item-arrangement",
                title = contentType != null ? contentType.Name : "Doc Type",
                description = contentType != null ? contentType.Description : string.Empty
            };
        }

        [HttpGet]
        public object GetDataTypePreValues(string dtdId)
        {
            Guid guidDtdId;
            int intDtdId;

            IDataType dtd;

            // Parse the ID
            if (int.TryParse(dtdId, out intDtdId))
            {
                // Do nothing, we just want the int ID
                dtd = Current.Services.DataTypeService.GetDataType(intDtdId);
            }
            else if (Guid.TryParse(dtdId, out guidDtdId))
            {
                dtd = Current.Services.DataTypeService.GetDataType(guidDtdId);
            }
            else
            {
                return null;
            }

            if (dtd == null)
                return null;

            // Convert to editor config
            var dataType = Current.Services.DataTypeService.GetDataType(dtd.Id);
            var propEditor = dataType.Editor;
            var content = propEditor.GetValueEditor().ConvertDbToString(new PropertyType(dataType), dataType.Configuration, Current.Services.DataTypeService);
            return content;
        }

        [HttpPost]
        public HttpResponseMessage GetPreviewMarkup([FromBody] PreviewData data, [FromUri] int pageId)
        {
            var page = default(IPublishedContent);

            // If the page is new, then the ID will be zero
            if (pageId > 0)
            {
                // Get page container node
                page = Umbraco.Content(pageId);
                if (page == null)
                {
                    // If unpublished, then fake PublishedContent
                    page = new UnpublishedContent(pageId, Services);
                }
            }

            // Set the culture for the preview
            if (page != null)
            {
                if (((UnpublishedContent)page).GetCulture() != null)
                {
                    var culture = new CultureInfo(((UnpublishedContent)page).GetCulture().Culture);
                    System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                    System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                }
            }

            // Get content node object
            var content = DocTypeGridEditorHelper.ConvertValueToContent(data.Id, data.ContentTypeAlias, data.Value);

            // Construct preview model
            var model = new PreviewModel
            {
                Page = page,
                Item = content,
                EditorAlias = data.EditorAlias,
                PreviewViewPath = data.PreviewViewPath,
                ViewPath = data.ViewPath
            };

            // Render view
            var partialName = "~/App_Plugins/DocTypeGridEditor/Render/DocTypeGridEditorPreviewer.cshtml";
            var markup = Helpers.ViewHelper.RenderPartial(partialName, model, UmbracoContext.HttpContext);

            // Return response
            var response = new HttpResponseMessage
            {
                Content = new StringContent(markup ?? string.Empty)
            };

            response.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Text.Html);

            return response;
        }
    }
}