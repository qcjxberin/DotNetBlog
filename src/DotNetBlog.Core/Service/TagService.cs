﻿using DotNetBlog.Core.Data;
using DotNetBlog.Core.Model.Tag;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetBlog.Core.Extensions;
using DotNetBlog.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace DotNetBlog.Core.Service
{
    public class TagService
    {
        private BlogContext BlogContext { get; set; }

        public TagService(BlogContext blogContext)
        {
            BlogContext = blogContext;
        }

        public async Task<List<TagModel>> All()
        {
            return await BlogContext.QueryAllTagFromCache();
        }

        public async Task<PagedResult<TagModel>> Query(int pageIndex, int pageSize, string keywords)
        {
            var query = (await this.All()).AsQueryable();

            if (!string.IsNullOrWhiteSpace(keywords))
            {
                query = query.Where(t => t.Keyword.Contains(keywords));
            }

            int total = query.Count();

            var list = query.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

            return new PagedResult<TagModel>(list, total);
        }

        public async Task<TagModel> Get(string keyword)
        {
            return (await All()).FirstOrDefault(t => t.Keyword == keyword);
        }

        public async Task Delete(int[] idList)
        {
            var entityList = await this.BlogContext.Tags.Where(t => idList.Contains(t.ID)).ToListAsync();
            this.BlogContext.RemoveRange(entityList);
            await this.BlogContext.SaveChangesAsync();

            this.BlogContext.RemoveTagCache();
        }

        public async Task<OperationResult> Edit(int id, string keyword)
        {
            var all = await this.All();
            if (all.Any(t => t.Keyword == keyword && t.ID != id))
            {
                return OperationResult.Failure("标签名称重复");
            }

            var entity = await this.BlogContext.Tags.SingleOrDefaultAsync(t => t.ID == id);

            if(entity == null)
            {
                return OperationResult.Failure("标签不存在");
            }

            entity.Keyword = keyword;
            await this.BlogContext.SaveChangesAsync();

            this.BlogContext.RemoveTagCache();

            return new OperationResult();
        }
    }
}
