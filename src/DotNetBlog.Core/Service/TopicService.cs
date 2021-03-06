﻿using DotNetBlog.Core.Model;
using DotNetBlog.Core.Model.Topic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetBlog.Core.Extensions;
using DotNetBlog.Core.Data;
using DotNetBlog.Core.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DotNetBlog.Core.Service
{
    public class TopicService
    {
        private BlogContext BlogContext { get; set; }

        private IMemoryCache Cache { get; set; }

        public TopicService(BlogContext blogContext, IMemoryCache cache)
        {
            BlogContext = blogContext;
            Cache = cache;
        }

        public async Task<OperationResult<int>> Add(string title, string content, Enums.TopicStatus status = Enums.TopicStatus.Normal, int[] categoryList = null, string[] tagList = null, string alias = null, string summary = null, DateTime? date = null, bool? allowComment = true)
        {
            categoryList = (categoryList ?? new int[0]).Distinct().ToArray();
            tagList = (tagList ?? new string[0]).Distinct().ToArray();

            List<Category> categoryEntityList = await BlogContext.Categories.Where(t => categoryList.Contains(t.ID)).ToListAsync();
            List<Tag> tagEntityList = await BlogContext.Tags.Where(t => tagList.Contains(t.Keyword)).ToListAsync();

            foreach (var tag in tagList)
            {
                if (!tagEntityList.Any(t => t.Keyword == tag))
                {
                    var tagEntity = new Tag
                    {
                        Keyword = tag
                    };
                    BlogContext.Tags.Add(tagEntity);
                    tagEntityList.Add(tagEntity);
                }
            }

            var topic = new Topic
            {
                Alias = alias,
                AllowComment = allowComment == true,
                Content = content,
                CreateDate = DateTime.Now,
                CreateUserID = 1,
                EditDate = date ?? DateTime.Now,
                EditUserID = 1,
                Status = status,
                Summary = summary,
                Title = title
            };
            BlogContext.Topics.Add(topic);

            List<CategoryTopic> categoryTopicList = categoryEntityList.Select(t => new CategoryTopic
            {
                Category = t,
                Topic = topic
            }).ToList();
            BlogContext.CategoryTopics.AddRange(categoryTopicList);

            List<TagTopic> tagTopicList = tagEntityList.Select(t => new TagTopic
            {
                Tag = t,
                Topic = topic
            }).ToList();
            BlogContext.TagTopics.AddRange(tagTopicList);

            await BlogContext.SaveChangesAsync();

            BlogContext.RemoveCategoryCache();
            BlogContext.RemoveTagCache();

            return new OperationResult<int>(topic.ID);
        }

        public async Task<OperationResult> Edit(int id, string title, string content, Enums.TopicStatus status = Enums.TopicStatus.Normal, int[] categoryList = null, string[] tagList = null, string alias = null, string summary = null, DateTime? date = null, bool? allowComment = true)
        {
            var entity = await BlogContext.Topics.SingleOrDefaultAsync(t => t.ID == id);
            if (entity == null)
            {
                return OperationResult.Failure("文章不存在");
            }

            using (var tran = await BlogContext.Database.BeginTransactionAsync())
            {
                List<CategoryTopic> deletedCategoryTopicList = await BlogContext.CategoryTopics.Where(t => t.TopicID == id).ToListAsync();
                BlogContext.RemoveRange(deletedCategoryTopicList);
                List<TagTopic> deletedTagTopicList = await BlogContext.TagTopics.Where(t => t.TopicID == id).ToListAsync();
                BlogContext.RemoveRange(deletedTagTopicList);

                await BlogContext.SaveChangesAsync();

                categoryList = (categoryList ?? new int[0]).Distinct().ToArray();
                tagList = (tagList ?? new string[0]).Distinct().ToArray();

                List<Category> categoryEntityList = await BlogContext.Categories.Where(t => categoryList.Contains(t.ID)).ToListAsync();
                List<Tag> tagEntityList = await BlogContext.Tags.Where(t => tagList.Contains(t.Keyword)).ToListAsync();

                foreach (var tag in tagList)
                {
                    if (!tagEntityList.Any(t => t.Keyword == tag))
                    {
                        var tagEntity = new Tag
                        {
                            Keyword = tag
                        };
                        BlogContext.Tags.Add(tagEntity);
                        tagEntityList.Add(tagEntity);
                    }
                }

                entity.Title = title;
                entity.Content = content;
                entity.Status = status;
                entity.Alias = alias;
                entity.Summary = summary;
                entity.EditDate = date ?? DateTime.Now;
                entity.AllowComment = allowComment == true;

                List<CategoryTopic> categoryTopicList = categoryEntityList.Select(t => new CategoryTopic
                {
                    Category = t,
                    Topic = entity
                }).ToList();
                BlogContext.CategoryTopics.AddRange(categoryTopicList);

                List<TagTopic> tagTopicList = tagEntityList.Select(t => new TagTopic
                {
                    Tag = t,
                    Topic = entity
                }).ToList();
                BlogContext.TagTopics.AddRange(tagTopicList);

                await BlogContext.SaveChangesAsync();

                tran.Commit();
            }

            BlogContext.RemoveCategoryCache();
            BlogContext.RemoveTagCache();

            return new OperationResult();
        }

        /// <summary>
        /// 查询正常状态的文章列表
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="status"></param>
        /// <param name="keywords"></param>
        /// <returns></returns>
        public async Task<PagedResult<TopicModel>> QueryNotTrash(int pageIndex, int pageSize, Enums.TopicStatus? status, string keywords)
        {
            var query = BlogContext.Topics.Where(t => t.Status != Enums.TopicStatus.Trash);

            if (status.HasValue)
            {
                query = query.Where(t => t.Status == status.Value);
            }
            if (!string.IsNullOrWhiteSpace(keywords))
            {
                query = query.Where(t => t.Title.Contains(keywords));
            }

            int total = await query.CountAsync();

            Topic[] entityList = await query.OrderByDescending(t => t.EditDate).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArrayAsync();

            List<TopicModel> modelList = await Transform(entityList);

            return new PagedResult<TopicModel>(modelList, total);
        }

        /// <summary>
        /// 根据分类，查询文章列表
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="categoryID"></param>
        /// <returns></returns>
        public async Task<PagedResult<TopicModel>> QueryByCategory(int pageIndex, int pageSize, int categoryID)
        {
            var topicIDQuery = BlogContext.CategoryTopics.Where(t => t.CategoryID == categoryID).Select(t => t.TopicID);
            var query = BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published && topicIDQuery.Contains(t.ID));

            int total = await query.CountAsync();

            Topic[] entityList = await query.OrderByDescending(t => t.EditDate).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArrayAsync();

            List<TopicModel> modelList = await Transform(entityList);

            return new PagedResult<TopicModel>(modelList, total);
        }

        /// <summary>
        /// 根据标签，查询文章列表
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="keyword"></param>
        /// <returns></returns>
        public async Task<PagedResult<TopicModel>> QueryByTag(int pageIndex, int pageSize, string keyword)
        {
            var topicIDQuery = from tag in BlogContext.Tags
                               where tag.Keyword == keyword
                               join tagTopic in BlogContext.TagTopics on tag.ID equals tagTopic.TagID
                               select tagTopic.TopicID;

            var query = BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published && topicIDQuery.Contains(t.ID));

            int total = await query.CountAsync();

            Topic[] entityList = await query.OrderByDescending(t => t.EditDate).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArrayAsync();

            List<TopicModel> modelList = await Transform(entityList);

            return new PagedResult<TopicModel>(modelList, total);
        }

        /// <summary>
        /// 根据月份，查询文章列表
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="year"></param>
        /// <param name="month"></param>
        /// <returns></returns>
        public async Task<PagedResult<TopicModel>> QueryByMonth(int pageIndex, int pageSize, int year, int month)
        {
            var startDate = new DateTime(year, month, 1, 0, 0, 0);
            var endDate = startDate.AddMonths(1);

            var query = BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published)
                .Where(t => t.EditDate >= startDate && t.EditDate < endDate);

            int total = await query.CountAsync();

            Topic[] entityList = await query.OrderByDescending(t => t.EditDate).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArrayAsync();

            List<TopicModel> modelList = await Transform(entityList);

            return new PagedResult<TopicModel>(modelList, total);
        }

        /// <summary>
        /// 查询最新发布的文章
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        public async Task<List<TopicModel>> QueryRecent(int count)
        {
            var query = BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published)
                .OrderByDescending(t => t.EditDate)
                .Take(count);

            Topic[] entityList = await query.ToArrayAsync();

            List<TopicModel> modelList = await Transform(entityList);

            return modelList;
        }

        /// <summary>
        /// 得到文章实体
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<TopicModel> Get(int id)
        {
            var entity = await BlogContext.Topics.SingleOrDefaultAsync(t => t.ID == id);

            if (entity == null)
            {
                return null;
            }

            return (await Transform(entity)).First();
        }

        /// <summary>
        /// 得到前一篇文章
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<TopicModel> GetPrev(TopicModel topic)
        {
            var entity = await BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published && t.EditDate < topic.Date)
                .OrderByDescending(t => t.EditDate)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                return null;
            }

            return (await Transform(entity)).First();
        }

        /// <summary>
        /// 得到下一篇文章
        /// </summary>
        /// <param name="topic"></param>
        /// <returns></returns>
        public async Task<TopicModel> GetNext(TopicModel topic)
        {
            var entity = await BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published && t.EditDate > topic.Date)
                .OrderBy(t => t.EditDate)
                .FirstOrDefaultAsync();

            if (entity == null)
            {
                return null;
            }

            return (await Transform(entity)).First();
        }

        /// <summary>
        /// 查询关联文章
        /// </summary>
        /// <param name="topic"></param>
        /// <returns></returns>
        public async Task<List<TopicModel>> QueryRelated(TopicModel topic)
        {
            string cacheKey = $"Cache_RelatedTopic_{topic.ID}";

            var list = Cache.Get<List<TopicModel>>(cacheKey);
            if (list == null)
            {
                if (topic.Tags.Length == 0 && topic.Categories.Length == 0)
                {
                    list = new List<TopicModel>();
                    Cache.Set(cacheKey, list);
                    return list;
                }

                var query = BlogContext.Topics.Where(t => t.Status == Enums.TopicStatus.Published && t.ID != topic.ID);
                if (topic.Tags.Length > 0)
                {
                    var topicIDQuery = from tag in BlogContext.Tags
                                       where topic.Tags.Contains(tag.Keyword)
                                       join tagTopic in BlogContext.TagTopics on tag.ID equals tagTopic.TagID
                                       select tagTopic.TopicID;

                    query = query.Where(t => topicIDQuery.Contains(t.ID));
                }
                if (topic.Categories.Length > 0)
                {
                    var categoryIDList = topic.Categories.Select(t => t.ID).ToArray();
                    var topicIDQuery = from category in BlogContext.Categories
                                       where categoryIDList.Contains(category.ID)
                                       join categoryTopic in BlogContext.CategoryTopics on category.ID equals categoryTopic.CategoryID
                                       select categoryTopic.TopicID;

                    query = query.Where(t => topicIDQuery.Contains(t.ID));
                }

                var entityList = await query.OrderByDescending(t => t.EditDate).Take(10).ToArrayAsync();

                list = await Transform(entityList);

                Cache.Set(cacheKey, list);
            }

            return list;
        }

        /// <summary>
        /// 得到按月份的文章统计结果
        /// </summary>
        /// <returns></returns>
        public async Task<List<MonthStatisticsModel>> QueryMonthStatistics()
        {
            return await BlogContext.QueryMonthStatisticsFromCache();
        }

        /// <summary>
        /// 批量修改文章的状态
        /// </summary>
        /// <param name="idList"></param>
        /// <param name="status"></param>
        /// <returns></returns>
        public async Task BatchUpdateStatus(int[] idList, Enums.TopicStatus status)
        {
            var topicList = await BlogContext.Topics.Where(t => idList.Contains(t.ID)).ToListAsync();

            topicList.ForEach(topic =>
            {
                topic.Status = status;
            });

            await BlogContext.SaveChangesAsync();

            BlogContext.RemoveCategoryCache();
            BlogContext.RemoveTagCache();
        }

        private async Task<List<TopicModel>> Transform(params Topic[] entityList)
        {
            if (entityList == null)
            {
                return null;
            }

            int[] idList = entityList.Select(t => t.ID).ToArray();

            List<CategoryTopic> categoryTopicList = await BlogContext.CategoryTopics.Include(t => t.Category).Where(t => idList.Contains(t.TopicID)).ToListAsync();
            List<TagTopic> tagTopicList = await BlogContext.TagTopics.Include(t => t.Tag).Where(t => idList.Contains(t.TopicID)).ToListAsync();

            List<TopicModel> result = entityList.Select(entity =>
            {
                var model = AutoMapper.Mapper.Map<TopicModel>(entity);
                model.Categories = categoryTopicList.Where(category => category.TopicID == entity.ID)
                    .Select(category => new TopicModel.CategoryModel
                    {
                        ID = category.CategoryID,
                        Name = category.Category.Name
                    }).ToArray();
                model.Tags = tagTopicList.Where(tag => tag.TopicID == entity.ID)
                    .Select(tag => tag.Tag.Keyword)
                    .ToArray();
                return model;
            }).ToList();

            return result;
        }
    }
}
