# Deployment

1. Find out what level of publish trimming would work. 

Prep

# Links

Yaml deserialisation https://www.codesuji.com/2022/11/13/Yaml-and-F/

# Creating a binary

dotnet publish -c Release --self-contained -r osx-x64
mv /Users/rogerwillis/Desktop/side-project/Blog/Generator/bin/Release/net7.0/osx-x64/publish/Generator Binary
./Binary --input-directory data --output-directory output --template-path templates/post.tmpl

# Running directly

dotnet run --project Generator --input-directory data --output-directory output --template-path templates/post.tmpl

# Site map

1. Home / - last 10 blog posts
   List of 10 post objects ordered by date descending.
2. Post /yyyy/mm/dd/slug - permalink for each post
   The Post ovject.
3. About /about/ - about the website and me
   No data.
4. Archive /archive/ - A page for each year/month combination there are posts for. Basically a long list of post titles with tags perhaps?
   List objects containing post year post month post title and post tags e.g. [(year, [(month, [post])])]
5. Tags /tags/ /tags/tagName - a page showing all tags and the post title and date for those tags
   [(tag, [post])]

# Design thinking

Some pages have data associated with them. Others are just static pages with no additional data.

The data for each type of page is also different, for example, the archive page just needs URLs 
and dates of all the posts. It doesn't actually need the post content. So we probably need a geneerator
for each page type. The main thing each generator does is prepare the data in a way which is useful for
for the page template.

We define a page with 

1. Need a Page structure of template path, url generator
2. For all templates in the pages folder, we create a generator.

2. Each Page structure can generate multiple html files. E.g. Post generates one for each post. but home will just generate one page.
3. Some pages don't have a data strcuture. E.g. about page is just static content.
4. To generate te site we first need to get all the raw post data.
5. We then assemble a list of all the page types.
6. We then pipe in the data to each page type.

The generator function can be the same for each page - we just need to change the template file and the data strcuture which is passed in.

Also need to generate the post URL as well.

page name | template name | url generator | page generator

We need a function to generate a single page given some data
We also need a function to generate multiple pages given some data



